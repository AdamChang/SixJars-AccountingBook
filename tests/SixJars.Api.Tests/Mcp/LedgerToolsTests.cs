using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SixJars.Domain.Books;
using SixJars.Domain.Transactions;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests.Mcp;

/// <summary>
/// 唯讀 ledger tools（spec §7）：以 SDK 的 McpClient 走真的 protocol。重點是讓 Claude 不必自己推算：
/// 名稱已解析好，金額是「這個帳戶上」的帶號變動（刷卡負、折抵與繳卡費正）。
/// </summary>
public class LedgerToolsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task List_books_returns_only_member_books()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var mine = await factory.SeedBookAsync(Ct);
        await factory.SeedBookAsync(Ct, ownerSubject: "someone-else");
        await using var client = await ConnectAsync(factory);

        var books = await CallAsync(client, "list_books", []);

        books.EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).Should().Equal(mine.Id.Value);
    }

    [Fact]
    public async Task List_accounts_includes_credit_card_type()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        await using var client = await ConnectAsync(factory);

        var accounts = await CallAsync(client, "list_accounts", new() { ["bookId"] = book.Id.Value });

        accounts.EnumerateArray().Should().ContainSingle(a => a.GetProperty("name").GetString() == "國泰Combo卡"
            && a.GetProperty("type").GetString() == "CreditCard" && !a.GetProperty("archived").GetBoolean());
    }

    [Fact]
    public async Task Account_transactions_resolve_names_and_signed_amount_on_account()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var card = await SeedCardTransactionsAsync(factory, book.Id);
        await using var client = await ConnectAsync(factory);

        var result = await CallAsync(client, "list_account_transactions", new()
        {
            ["bookId"] = book.Id.Value, ["accountId"] = card, ["from"] = "2026-01-01", ["to"] = "2026-01-31",
        });

        result.GetProperty("account").GetString().Should().Be("國泰Combo卡");
        result.GetProperty("count").GetInt32().Should().Be(3);
        result.GetProperty("total").GetDecimal().Should().Be(-99m + 250m + 500m);
        var rows = result.GetProperty("transactions").EnumerateArray().ToList();
        rows.Select(r => r.GetProperty("date").GetString()).Should().Equal("2026-01-05", "2026-01-06", "2026-01-20");

        rows[0].GetProperty("kind").GetString().Should().Be("Expense");
        rows[0].GetProperty("amount").GetDecimal().Should().Be(-99m);
        rows[0].GetProperty("mainCategory").GetString().Should().Be("主食");
        rows[0].GetProperty("subCategory").GetString().Should().Be("午餐");
        rows[0].GetProperty("note").GetString().Should().Be("Subway");

        rows[1].GetProperty("amount").GetDecimal().Should().Be(250m, "點數折抵在卡上是正數");
        // SDK 序列化時省略 null 欄位，兩種寫法都算「沒有子分類」。
        (rows[1].TryGetProperty("subCategory", out var sub) ? sub.ValueKind : JsonValueKind.Null).Should().Be(JsonValueKind.Null);

        rows[2].GetProperty("kind").GetString().Should().Be("CardPayment");
        rows[2].GetProperty("amount").GetDecimal().Should().Be(500m, "繳卡費減少卡債，在卡上是正數");
        rows[2].GetProperty("counterAccount").GetString().Should().Be("國泰世華銀行");
    }

    [Fact]
    public async Task Card_payment_is_negative_on_paying_bank()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        await SeedCardTransactionsAsync(factory, book.Id);
        await using var client = await ConnectAsync(factory);

        var result = await CallAsync(client, "list_account_transactions", new()
        {
            ["bookId"] = book.Id.Value, ["accountId"] = book.FindAccount("國泰世華銀行")!.Id.Value, ["from"] = "2026-01-01", ["to"] = "2026-01-31",
        });

        var payment = result.GetProperty("transactions").EnumerateArray().Single();
        payment.GetProperty("amount").GetDecimal().Should().Be(-500m, "金額是這個帳戶上的分錄，不是交易金額（交易金額恆為正）");
        payment.GetProperty("counterAccount").GetString().Should().Be("國泰Combo卡");
    }

    [Theory]
    [InlineData("2026-01-01", "2026-05-02", "120")]
    [InlineData("2026-02-01", "2026-01-01", "from")]
    public async Task Invalid_period_is_a_tool_error(string from, string to, string messageFragment)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        await using var client = await ConnectAsync(factory);

        var result = await client.CallToolAsync("list_account_transactions", new Dictionary<string, object?>
        {
            ["bookId"] = book.Id.Value, ["accountId"] = book.FindAccount("國泰Combo卡")!.Id.Value, ["from"] = from, ["to"] = to,
        }, cancellationToken: Ct);

        result.IsError.Should().BeTrue();
        Text(result).Should().Contain(messageFragment);
    }

    [Fact]
    public async Task Other_users_book_is_not_found()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var other = await factory.SeedBookAsync(Ct, ownerSubject: "someone-else");
        await using var client = await ConnectAsync(factory);

        var result = await client.CallToolAsync("list_accounts", new Dictionary<string, object?> { ["bookId"] = other.Id.Value }, cancellationToken: Ct);

        result.IsError.Should().BeTrue();
        Text(result).Should().Contain("找不到帳本");
    }

    [Fact]
    public async Task Account_of_another_book_is_not_found()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        await using var client = await ConnectAsync(factory);

        var result = await client.CallToolAsync("list_account_transactions", new Dictionary<string, object?>
        {
            ["bookId"] = book.Id.Value, ["accountId"] = Guid.NewGuid(), ["from"] = "2026-01-01", ["to"] = "2026-01-31",
        }, cancellationToken: Ct);

        result.IsError.Should().BeTrue();
        Text(result).Should().Contain("找不到帳戶");
    }

    [Fact]
    public async Task Tools_are_read_only_and_reconcile_guidance_is_published()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await using var client = await ConnectAsync(factory);

        var tools = await client.ListToolsAsync(cancellationToken: Ct);
        var prompts = await client.ListPromptsAsync(cancellationToken: Ct);

        tools.Select(t => t.Name).Should().BeEquivalentTo("list_books", "list_accounts", "list_account_transactions");
        tools.Should().OnlyContain(t => t.ProtocolTool.Annotations!.ReadOnlyHint == true);
        prompts.Select(p => p.Name).Should().Contain("reconcile_credit_card_statement");
        client.ServerInstructions.Should().Contain("消費日");
    }

    /// <summary>卡上：1/5 支出 -99（主食／午餐）、1/6 點數折抵 +250（主食，無子分類）、1/20 由銀行繳卡費 500；另有一筆現金支出，不應出現。</summary>
    private static async Task<Guid> SeedCardTransactionsAsync(ApiFactory factory, Domain.Common.BookId bookId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
        var book = await db.Books.SingleAsync(b => b.Id == bookId, Ct);
        var card = book.FindAccount("國泰Combo卡")!.Id;
        var transactions = new TransactionFactory(book);
        db.Transactions.AddRange(
            transactions.Expense(new DateOnly(2026, 1, 5), card, book.FindCategory("主食", "午餐")!.Id, -99m, "Subway"),
            transactions.Expense(new DateOnly(2026, 1, 6), card, book.FindCategory("主食")!.Id, 250m, "小樹點折抵"),
            transactions.CardPayment(new DateOnly(2026, 1, 20), book.FindAccount("國泰世華銀行")!.Id, card, 500m),
            transactions.Expense(new DateOnly(2026, 1, 7), book.FindAccount("現金")!.Id, book.FindCategory("主食")!.Id, -60m));
        await db.SaveChangesAsync(Ct);
        return card.Value;
    }

    private static async Task<McpClient> ConnectAsync(ApiFactory factory, string subject = ApiFactory.DefaultSubject)
    {
        var (_, token) = await McpTestTokens.IssueAsync(factory, Ct, subject);
        var http = McpTestTokens.CreateBearerClient(factory, token);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri("https://localhost/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http, NullLoggerFactory.Instance, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport, cancellationToken: Ct);
    }

    private static async Task<JsonElement> CallAsync(McpClient client, string tool, Dictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(tool, arguments, cancellationToken: Ct);
        result.IsError.Should().NotBe(true, Text(result));
        return JsonDocument.Parse(Text(result)).RootElement;
    }

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
}
