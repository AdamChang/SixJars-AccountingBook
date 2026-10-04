using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Application.Auditing;
using SixJars.Application.Planning;
using SixJars.Application.Transactions;
using SixJars.Domain.Books;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

/// <summary>稽核記錄（ADR 0006）：每個寫入 endpoint 恰好留下一筆記錄，內容是修改前後的完整快照；寫入失敗時不留記錄。</summary>
public class AuditTrailTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 2, 28, 9, 30, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("AddAccount", AuditAction.Create, "Account")]
    [InlineData("AddPlanningFund", AuditAction.Create, "PlanningFund")]
    [InlineData("AddCategory", AuditAction.Create, "Category")]
    [InlineData("CreateTransaction", AuditAction.Create, "Transaction")]
    [InlineData("UpdateTransaction", AuditAction.Update, "Transaction")]
    [InlineData("DeleteTransaction", AuditAction.Delete, "Transaction")]
    [InlineData("CreatePlannedExpense", AuditAction.Create, "PlannedExpense")]
    [InlineData("UpdatePlannedExpense", AuditAction.Update, "PlannedExpense")]
    [InlineData("DeletePlannedExpense", AuditAction.Delete, "PlannedExpense")]
    [InlineData("PayPlannedExpense", AuditAction.Update, "PlannedExpense")]
    public async Task Each_write_endpoint_leaves_exactly_one_entry(string operation, AuditAction action, string entityType)
    {
        await using var factory = await CreateFactoryAsync();
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        // 前置資料（例如修改前要先有一筆交易）也會留下記錄，所以先準備好，再取基準點。
        var act = await PrepareAsync(operation, client, book);
        var baseline = await EntriesAsync(factory);

        var entityId = await act();

        var added = (await EntriesAsync(factory)).ExceptBy(baseline.Select(e => e.Id), e => e.Id).ToList();
        var entry = added.Should().ContainSingle(e => e.EntityId == entityId).Subject;
        entry.Action.Should().Be(action);
        entry.EntityType.Should().Be(entityType);
        entry.BookId.Should().Be(book.Id.Value);
        entry.ActorSubject.Should().Be(ApiFactory.DefaultSubject);
        entry.At.Should().Be(Now);
        // 新增沒有修改前、刪除沒有修改後；其他都兩者皆有。
        (entry.Before is null).Should().Be(action == AuditAction.Create);
        (entry.After is null).Should().Be(action == AuditAction.Delete);

        if (operation == "PayPlannedExpense")
        {
            // 付款另外建立一筆交易，留下交易的 Create；預定支出本身仍然只有一筆記錄。
            added.Should().HaveCount(2);
            var payment = added.Single(e => e.EntityId != entityId);
            payment.Action.Should().Be(AuditAction.Create);
            payment.EntityType.Should().Be("Transaction");
            Snapshot(entry.Before).GetProperty("isPaid").GetBoolean().Should().BeFalse();
            Snapshot(entry.After).GetProperty("paidTransactionId").GetGuid().Should().Be(payment.EntityId);
        }
        else
        {
            added.Should().HaveCount(1);
        }

        if (operation == "UpdatePlannedExpense")
        {
            // before 必須在 Update 之前取得，否則兩邊都是修改後的內容。
            Snapshot(entry.Before).GetProperty("estimatedAmount").GetDecimal().Should().Be(-3000m);
            Snapshot(entry.After).GetProperty("estimatedAmount").GetDecimal().Should().Be(-3500m);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task Failed_write_leaves_no_entry(HttpStatusCode expected)
    {
        await using var factory = await CreateFactoryAsync();
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var created = await CreateTransactionAsync(client, book, LunchInput(book, -100m, "午餐"));
        var url = $"/api/books/{book.Id.Value}/transactions/{created.Id}";
        // 409：先改一次讓版本前進，再用舊版本修改；422：轉帳到信用卡違反業務規則。
        if (expected == HttpStatusCode.Conflict)
        {
            (await client.PutAsJsonAsync(url, new { version = created.Version, input = LunchInput(book, -110m, "第一次") }, ApiJson.Options, Ct))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var baseline = await EntriesAsync(factory);

        var response = expected == HttpStatusCode.Conflict
            ? await client.PutAsJsonAsync(url, new { version = created.Version, input = LunchInput(book, -120m, "第二次") }, ApiJson.Options, Ct)
            : await client.PostAsJsonAsync($"/api/books/{book.Id.Value}/transactions",
                new
                {
                    kind = "Transfer", date = "2026-01-05", amount = 500m,
                    accountId = book.FindAccount("國泰世華銀行")!.Id.Value, counterAccountId = book.FindAccount("國泰Combo卡")!.Id.Value,
                }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(expected);
        (await EntriesAsync(factory)).Should().HaveSameCount(baseline);
    }

    [Fact]
    public async Task Update_entry_has_before_and_after_snapshots()
    {
        await using var factory = await CreateFactoryAsync();
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var created = await CreateTransactionAsync(client, book, LunchInput(book, -100m, "午餐便當"));

        var response = await client.PutAsJsonAsync($"/api/books/{book.Id.Value}/transactions/{created.Id}",
            new { version = created.Version, input = LunchInput(book, -150m, "午餐便當<加蛋>") }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entry = (await EntriesAsync(factory)).Single(e => e.EntityId == created.Id && e.Action == AuditAction.Update);
        var before = Snapshot(entry.Before);
        var after = Snapshot(entry.After);
        // before 必須在 ReplaceWith 之前取得，否則會拿到修改後的內容。
        before.GetProperty("amount").GetDecimal().Should().Be(-100m);
        after.GetProperty("amount").GetDecimal().Should().Be(-150m);
        // 快照包含分錄。
        before.GetProperty("postings")[0].GetProperty("amount").GetDecimal().Should().Be(-100m);
        after.GetProperty("postings")[0].GetProperty("amount").GetDecimal().Should().Be(-150m);
        // 修改前的快照帶讀到的版本；修改後的版本要到存檔才由資料庫產生，一律記為 0（見 AuditSnapshots）。
        before.GetProperty("version").GetUInt32().Should().Be(created.Version);
        after.GetProperty("version").GetUInt32().Should().Be(0);
        // 列舉存成名稱、中文直接可讀。
        after.GetProperty("kind").GetString().Should().Be("Expense");
        entry.After.Should().Contain("午餐便當<加蛋>");
    }

    private Task<ApiFactory> CreateFactoryAsync() =>
        ApiFactory.CreateAsync(postgres, Ct, services => services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now)));

    /// <summary>準備前置資料，回傳要測的寫入操作；操作回傳被寫入的實體 Id。</summary>
    private static async Task<Func<Task<Guid>>> PrepareAsync(string operation, HttpClient client, Book book)
    {
        var bookUrl = $"/api/books/{book.Id.Value}";
        switch (operation)
        {
            case "AddAccount":
                return () => PostForIdAsync(client, $"{bookUrl}/accounts", new { name = "郵局", type = "Bank", openingBalance = 0m });
            case "AddPlanningFund":
                return () => PostForIdAsync(client, $"{bookUrl}/planning-funds", new { name = "旅遊基金", openingBalance = 0m });
            case "AddCategory":
                return () => PostForIdAsync(client, $"{bookUrl}/categories", new { name = "交通", kind = "Expense", nature = "Floating" });
            case "CreateTransaction":
                return async () => (await CreateTransactionAsync(client, book, LunchInput(book, -100m, "午餐"))).Id;
            case "UpdateTransaction":
            {
                var created = await CreateTransactionAsync(client, book, LunchInput(book, -100m, "午餐"));
                return async () =>
                {
                    var response = await client.PutAsJsonAsync($"{bookUrl}/transactions/{created.Id}",
                        new { version = created.Version, input = LunchInput(book, -150m, "午餐") }, ApiJson.Options, Ct);
                    response.StatusCode.Should().Be(HttpStatusCode.OK);
                    return created.Id;
                };
            }
            case "DeleteTransaction":
            {
                var created = await CreateTransactionAsync(client, book, LunchInput(book, -100m, "午餐"));
                return () => DeleteAsync(client, $"{bookUrl}/transactions/{created.Id}?version={created.Version}", created.Id);
            }
            case "CreatePlannedExpense":
                return async () => (await PlannedExpensesEndpointsTests.CreateAsync(
                    client, book, PlannedExpensesEndpointsTests.InsuranceInput(book, 202602, -3000m, "保險費"))).Id;
            case "UpdatePlannedExpense":
            {
                var planned = await CreatePlannedAsync(client, book);
                return async () =>
                {
                    var response = await client.PutAsJsonAsync($"{PlannedExpensesEndpointsTests.Url(book)}/{planned.Id}",
                        new { version = planned.Version, input = PlannedExpensesEndpointsTests.InsuranceInput(book, 202602, -3500m, "保險費") },
                        ApiJson.Options, Ct);
                    response.StatusCode.Should().Be(HttpStatusCode.OK);
                    return planned.Id;
                };
            }
            case "DeletePlannedExpense":
            {
                var planned = await CreatePlannedAsync(client, book);
                return () => DeleteAsync(client, $"{PlannedExpensesEndpointsTests.Url(book)}/{planned.Id}?version={planned.Version}", planned.Id);
            }
            case "PayPlannedExpense":
            {
                var planned = await CreatePlannedAsync(client, book);
                return async () =>
                {
                    var response = await client.PostAsJsonAsync($"{PlannedExpensesEndpointsTests.Url(book)}/{planned.Id}/pay",
                        new { version = planned.Version, date = "2026-02-20", accountId = book.FindAccount("國泰世華銀行")!.Id.Value, amount = -3000m },
                        ApiJson.Options, Ct);
                    response.StatusCode.Should().Be(HttpStatusCode.Created);
                    return planned.Id;
                };
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
        }
    }

    private static Task<PlannedExpenseDto> CreatePlannedAsync(HttpClient client, Book book) =>
        PlannedExpensesEndpointsTests.CreateAsync(client, book, PlannedExpensesEndpointsTests.InsuranceInput(book, 202602, -3000m, "保險費"));

    private static async Task<Guid> PostForIdAsync(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body, ApiJson.Options, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> DeleteAsync(HttpClient client, string url, Guid id)
    {
        (await client.DeleteAsync(url, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return id;
    }

    internal static object LunchInput(Book book, decimal amount, string note) => new
    {
        kind = "Expense", date = "2026-01-05", amount, note,
        accountId = book.FindAccount("現金")!.Id.Value, categoryId = book.FindCategory("主食", "午餐")!.Id.Value,
    };

    internal static async Task<TransactionDto> CreateTransactionAsync(HttpClient client, Book book, object input)
    {
        var response = await client.PostAsJsonAsync($"/api/books/{book.Id.Value}/transactions", input, ApiJson.Options, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TransactionDto>(ApiJson.Options, Ct))!;
    }

    /// <summary>直接讀資料表（不經 API），連同已刪除資料的記錄一起，依寫入順序排列。</summary>
    internal static async Task<List<AuditEntry>> EntriesAsync(ApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
        return await db.AuditEntries.IgnoreQueryFilters().AsNoTracking().OrderBy(e => e.At).ThenBy(e => e.Id).ToListAsync(Ct);
    }

    private static JsonElement Snapshot(string? json)
    {
        json.Should().NotBeNull();
        return JsonDocument.Parse(json!).RootElement;
    }
}
