using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SixJars.Application.Common;
using SixJars.Application.Transactions;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Transactions;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;
using static SixJars.Api.Tests.Mcp.McpToolClient;

namespace SixJars.Api.Tests.Mcp;

/// <summary>
/// 記帳用的讀取 tool（spec §6.2）：Claude 記帳前要先知道有哪些分類與標籤（用 Id），也要能用收據上的金額、備註或信件識別碼查出已記過的交易。
/// </summary>
public class BookkeepingToolsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Via = "mcp:claude.ai";

    private sealed class McpUser : ICurrentUser
    {
        public string Subject => ApiFactory.DefaultSubject;
        public string? Email => TestAuthHandler.EmailOf(ApiFactory.DefaultSubject);
        public string? Via => BookkeepingToolsTests.Via;
    }

    private static Dictionary<string, object?> Search(Book book, Dictionary<string, object?>? extra = null)
    {
        var args = new Dictionary<string, object?> { ["bookId"] = book.Id.Value, ["from"] = "2026-01-01", ["to"] = "2026-01-31" };
        foreach (var (key, value) in extra ?? [])
        {
            args[key] = value;
        }

        return args;
    }

    private static async Task SeedAsync(ApiFactory factory, BookId bookId, Func<Book, TransactionFactory, IEnumerable<Transaction>> build)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
        var book = await db.Books.SingleAsync(b => b.Id == bookId, Ct);
        db.Transactions.AddRange(build(book, new TransactionFactory(book)));
        await db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task List_categories_returns_tree_with_kind()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        await using var client = await ConnectAsync(factory);

        var result = await CallAsync(client, "list_categories", new() { ["bookId"] = book.Id.Value });

        var food = result.EnumerateArray().Single(c => c.GetProperty("name").GetString() == "主食");
        food.GetProperty("kind").GetString().Should().Be("Expense");
        food.GetProperty("id").GetGuid().Should().Be(book.FindCategory("主食")!.Id.Value);
        food.GetProperty("children").EnumerateArray().Should().ContainSingle(c => c.GetProperty("name").GetString() == "午餐"
            && c.GetProperty("id").GetGuid() == book.FindCategory("主食", "午餐")!.Id.Value && !c.GetProperty("archived").GetBoolean());
        result.EnumerateArray().Single(c => c.GetProperty("name").GetString() == "工作薪資").GetProperty("kind").GetString().Should().Be("Income");
    }

    [Fact]
    public async Task List_tags_returns_archived_flag()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
            var tracked = await db.Books.SingleAsync(b => b.Id == book.Id, Ct);
            tracked.AddTag("旅行", TagColor.Teal);
            var old = tracked.AddTag("舊專案", TagColor.Red);
            tracked.ArchiveTag(old.Id, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(Ct);
        }

        await using var client = await ConnectAsync(factory);

        var result = await CallAsync(client, "list_tags", new() { ["bookId"] = book.Id.Value });

        result.EnumerateArray().Select(t => (t.GetProperty("name").GetString(), t.GetProperty("archived").GetBoolean()))
            .Should().BeEquivalentTo([("旅行", false), ("舊專案", true)]);
    }

    [Fact]
    public async Task Search_by_amount_matches_absolute_value()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        await SeedAsync(factory, book.Id, (b, f) =>
        [
            f.Expense(new DateOnly(2026, 1, 5), b.FindAccount("現金")!.Id, b.FindCategory("主食", "午餐")!.Id, -120m, "Subway"),
            f.Expense(new DateOnly(2026, 1, 6), b.FindAccount("現金")!.Id, b.FindCategory("主食", "午餐")!.Id, -80m, "便當"),
        ]);
        await using var client = await ConnectAsync(factory);

        var result = await CallAsync(client, "search_transactions", Search(book, new() { ["amount"] = 120 }));

        result.GetProperty("count").GetInt32().Should().Be(1);
        var row = result.GetProperty("transactions").EnumerateArray().Single();
        row.GetProperty("amount").GetDecimal().Should().Be(-120m, "回傳的是帶號的交易金額，支出為負");
        row.GetProperty("account").GetString().Should().Be("現金");
        row.GetProperty("mainCategory").GetString().Should().Be("主食");
        row.GetProperty("subCategory").GetString().Should().Be("午餐");
    }

    [Fact]
    public async Task Search_by_keyword_is_case_insensitive_on_note()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        await SeedAsync(factory, book.Id, (b, f) =>
        [
            f.Expense(new DateOnly(2026, 1, 5), b.FindAccount("現金")!.Id, b.FindCategory("主食")!.Id, -120m, "Uber Eats 訂單 #123"),
            f.Expense(new DateOnly(2026, 1, 6), b.FindAccount("現金")!.Id, b.FindCategory("主食")!.Id, -80m, "便當"),
            f.Expense(new DateOnly(2026, 1, 7), b.FindAccount("現金")!.Id, b.FindCategory("主食")!.Id, -50m),
        ]);
        await using var client = await ConnectAsync(factory);

        var result = await CallAsync(client, "search_transactions", Search(book, new() { ["keyword"] = "uber eats" }));

        result.GetProperty("transactions").EnumerateArray().Select(t => t.GetProperty("note").GetString())
            .Should().Equal("Uber Eats 訂單 #123");
    }

    [Fact]
    public async Task Search_by_source_id_returns_created_via()
    {
        // ICurrentUser 換成帶 Via 的使用者只影響 command 的稽核與 CreatedVia；/mcp 的驗證走 McpBearerHandler，不受影響，所以同一個 factory 即可。
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct, s => s.AddScoped<ICurrentUser, McpUser>());
        var book = await factory.SeedBookAsync(Ct);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var input = new TransactionInput(TransactionKind.Expense, new DateOnly(2026, 1, 5), null, -120m, book.FindAccount("現金")!.Id.Value,
                null, book.FindCategory("主食", "午餐")!.Id.Value, null, null, "Subway");
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RecordTransaction(book.Id.Value, input, "<a@m>"), Ct);
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RecordTransaction(book.Id.Value, input, "<other@m>"), Ct);
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RecordTransaction(book.Id.Value, input with { Note = "無來源" }, null), Ct);
        }

        await using var client = await ConnectAsync(factory);

        var result = await CallAsync(client, "search_transactions", Search(book, new() { ["sourceId"] = "<a@m>" }));

        result.GetProperty("count").GetInt32().Should().Be(1, "另外兩筆（不同 sourceId、無 sourceId）要被排除");
        var row = result.GetProperty("transactions").EnumerateArray().Single();
        row.GetProperty("createdVia").GetString().Should().Be(Via);
        row.GetProperty("sourceId").GetString().Should().Be("<a@m>");
        row.GetProperty("id").GetGuid().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Search_period_over_120_days_is_rejected()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        await using var client = await ConnectAsync(factory);

        var result = await client.CallToolAsync("search_transactions",
            Search(book, new() { ["to"] = "2026-05-02" }), cancellationToken: Ct);

        result.IsError.Should().BeTrue();
        Text(result).Should().Contain("120");
    }

    [Fact]
    public async Task Read_tools_work_with_read_only_token()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        await using var client = await ConnectAsync(factory, scope: "ledger:read");

        var tools = await client.ListToolsAsync(cancellationToken: Ct);

        tools.Select(t => t.Name).Should().Contain(["list_categories", "list_tags", "search_transactions"]);
        tools.Where(t => t.Name is "list_categories" or "list_tags" or "search_transactions")
            .Should().OnlyContain(t => t.ProtocolTool.Annotations!.ReadOnlyHint == true);
        var tags = await CallAsync(client, "list_tags", new() { ["bookId"] = book.Id.Value });
        tags.ValueKind.Should().Be(JsonValueKind.Array);
    }

    private const string ReadWrite = "ledger:read ledger:write";

    private static Dictionary<string, object?> Add(Book book, Dictionary<string, object?>? extra = null)
    {
        var args = new Dictionary<string, object?>
        {
            ["bookId"] = book.Id.Value,
            ["kind"] = "Expense",
            ["date"] = "2026-03-10",
            ["amount"] = 120m,
            ["accountId"] = book.FindAccount("現金")!.Id.Value,
            ["categoryId"] = book.FindCategory("主食", "午餐")!.Id.Value,
        };
        foreach (var (key, value) in extra ?? [])
        {
            args[key] = value;
        }

        return args;
    }

    private static async Task<List<Transaction>> StoredTransactionsAsync(ApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
        return await db.Transactions.AsNoTracking().ToListAsync(Ct);
    }

    /// <summary>tool error 可能以 IsError 回應，也可能在 client 端擲 McpException；統一成 CallToolResult。</summary>
    private static async Task<CallToolResult> TryCallAsync(McpClient client, string tool, Dictionary<string, object?> args)
    {
        try
        {
            return await client.CallToolAsync(tool, args, cancellationToken: Ct);
        }
        catch (McpException exception)
        {
            return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = exception.Message }] };
        }
    }

    [Fact]
    public async Task Read_only_grant_does_not_list_write_tools()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await using var client = await ConnectAsync(factory, scope: "ledger:read");

        var names = (await client.ListToolsAsync(cancellationToken: Ct)).Select(t => t.Name).ToList();

        names.Should().NotContain("add_transaction");
        names.Should().Contain("validate_transaction");
    }

    [Fact]
    public async Task Read_only_grant_cannot_call_add_transaction()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        await using var client = await ConnectAsync(factory, scope: "ledger:read");

        var result = await TryCallAsync(client, "add_transaction", Add(book));

        result.IsError.Should().BeTrue();
        (await StoredTransactionsAsync(factory)).Should().BeEmpty();
    }

    [Fact]
    public async Task Write_grant_lists_add_transaction()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await using var client = await ConnectAsync(factory, scope: ReadWrite);

        var tools = await client.ListToolsAsync(cancellationToken: Ct);

        var add = tools.Single(t => t.Name == "add_transaction");
        add.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeFalse();
        add.ProtocolTool.Annotations.DestructiveHint.Should().BeFalse();
        tools.Select(t => t.Name).Should().Contain("validate_transaction");
    }

    [Fact]
    public async Task Add_expense_stores_negative_amount_and_created_via()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        await using var client = await ConnectAsync(factory, scope: ReadWrite);

        var result = await CallAsync(client, "add_transaction", Add(book, new() { ["note"] = "Uber Eats #123" }));

        result.GetProperty("duplicate").GetBoolean().Should().BeFalse();
        result.GetProperty("transaction").GetProperty("amount").GetDecimal().Should().Be(-120m);
        var stored = (await StoredTransactionsAsync(factory)).Should().ContainSingle().Subject;
        stored.Amount.Should().Be(-120m);
        stored.CreatedVia.Should().Be(Via);
        result.GetProperty("transaction").GetProperty("id").GetGuid().Should().Be(stored.Id.Value);
    }

    [Fact]
    public async Task Add_refund_stores_positive_amount()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        await using var client = await ConnectAsync(factory, scope: ReadWrite);

        await CallAsync(client, "add_transaction", Add(book, new() { ["refund"] = true }));

        (await StoredTransactionsAsync(factory)).Should().ContainSingle().Which.Amount.Should().Be(120m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Add_rejects_non_positive_amount(int amount)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        await using var client = await ConnectAsync(factory, scope: ReadWrite);

        var result = await TryCallAsync(client, "add_transaction", Add(book, new() { ["amount"] = amount }));

        result.IsError.Should().BeTrue();
        (await StoredTransactionsAsync(factory)).Should().BeEmpty();
    }

    [Fact]
    public async Task Add_rejects_unsupported_kind()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        await using var client = await ConnectAsync(factory, scope: ReadWrite);

        var result = await TryCallAsync(client, "add_transaction", Add(book, new() { ["kind"] = "FundAllocation" }));

        result.IsError.Should().BeTrue();
        Text(result).Should().Contain("CardPayment").And.Contain("Expense");
        (await StoredTransactionsAsync(factory)).Should().BeEmpty();
    }

    [Fact]
    public async Task Add_same_source_twice_returns_duplicate()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        await using var client = await ConnectAsync(factory, scope: ReadWrite);

        var first = await CallAsync(client, "add_transaction", Add(book, new() { ["sourceId"] = "<msg-1@mail>" }));
        var second = await CallAsync(client, "add_transaction", Add(book, new() { ["sourceId"] = "<msg-1@mail>" }));

        first.GetProperty("duplicate").GetBoolean().Should().BeFalse();
        second.GetProperty("duplicate").GetBoolean().Should().BeTrue();
        second.GetProperty("transaction").GetProperty("id").GetGuid().Should().Be(first.GetProperty("transaction").GetProperty("id").GetGuid());
        (await StoredTransactionsAsync(factory)).Should().ContainSingle();
    }

    [Fact]
    public async Task Add_card_payment_requires_counter_account()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        await using var client = await ConnectAsync(factory, scope: ReadWrite);

        var result = await TryCallAsync(client, "add_transaction", new()
        {
            ["bookId"] = book.Id.Value,
            ["kind"] = "CardPayment",
            ["date"] = "2026-03-10",
            ["amount"] = 500m,
            ["accountId"] = book.FindAccount("國泰世華銀行")!.Id.Value,
        });

        result.IsError.Should().BeTrue();
        (await StoredTransactionsAsync(factory)).Should().BeEmpty();
    }

    [Fact]
    public async Task Validate_returns_postings_with_names_and_does_not_write()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        await using var client = await ConnectAsync(factory, scope: "ledger:read");

        var result = await CallAsync(client, "validate_transaction", Add(book, new() { ["note"] = "午餐" }));

        var transaction = result.GetProperty("transaction");
        transaction.GetProperty("amount").GetDecimal().Should().Be(-120m);
        transaction.GetProperty("account").GetString().Should().Be("現金");
        transaction.GetProperty("mainCategory").GetString().Should().Be("主食");
        transaction.GetProperty("subCategory").GetString().Should().Be("午餐");
        transaction.GetProperty("postings").EnumerateArray()
            .Select(p => (p.GetProperty("account").GetString(), p.GetProperty("amount").GetDecimal()))
            .Should().Contain(("現金", -120m));
        result.GetProperty("locked").GetBoolean().Should().BeFalse();
        result.GetProperty("suspectedDuplicates").GetArrayLength().Should().Be(0);
        (await StoredTransactionsAsync(factory)).Should().BeEmpty();
    }

    private async Task<(ApiFactory Factory, MutableTimeProvider Clock)> CreateQuotaFactoryAsync(int maxWrites)
    {
        // 台北 2026-03-10 10:00（= 02:00Z）。
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 3, 10, 2, 0, 0, TimeSpan.Zero));
        var factory = new ApiFactory(await postgres.CreateConnectionStringAsync(Ct),
            testServices: s =>
            {
                s.RemoveAll<TimeProvider>();
                s.AddSingleton<TimeProvider>(clock);
            },
            settings: new Dictionary<string, string> { ["Mcp:MaxWritesPerDay"] = maxWrites.ToString() });
        return (factory, clock);
    }

    [Fact]
    public async Task Write_quota_is_enforced_per_day()
    {
        var (factory, clock) = await CreateQuotaFactoryAsync(2);
        await using var _ = factory;
        var book = await factory.SeedBookAsync(Ct);
        await using (var client = await ConnectAsync(factory, scope: ReadWrite))
        {
            await CallAsync(client, "add_transaction", Add(book, new() { ["sourceId"] = "a" }));
            await CallAsync(client, "add_transaction", Add(book, new() { ["sourceId"] = "b" }));

            var third = await TryCallAsync(client, "add_transaction", Add(book, new() { ["sourceId"] = "c" }));

            third.IsError.Should().BeTrue();
            Text(third).Should().Contain("上限");
        }

        // 隔天要重新連線：access token 的有效期間跟著時鐘走。
        clock.Advance(TimeSpan.FromHours(24));
        await using var nextDay = await ConnectAsync(factory, scope: ReadWrite);
        await CallAsync(nextDay, "add_transaction", Add(book, new() { ["sourceId"] = "c" }));

        (await StoredTransactionsAsync(factory)).Should().HaveCount(3);
    }

    [Fact]
    public async Task Duplicates_do_not_count_toward_quota()
    {
        var (factory, _) = await CreateQuotaFactoryAsync(1);
        await using var _ = factory;
        var book = await factory.SeedBookAsync(Ct);
        await using var client = await ConnectAsync(factory, scope: ReadWrite);

        await CallAsync(client, "add_transaction", Add(book, new() { ["sourceId"] = "a" }));
        var again = await CallAsync(client, "add_transaction", Add(book, new() { ["sourceId"] = "a" }));

        again.GetProperty("duplicate").GetBoolean().Should().BeTrue();
    }
}
