using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SixJars.Application.Auditing;
using SixJars.Application.Books;
using SixJars.Application.Planning;
using SixJars.Application.Transactions;
using SixJars.Domain.Books;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

/// <summary>
/// 鎖帳日（spec §3.1）：鎖帳日（含）以前的交易、歸屬月份最後一天在鎖帳日（含）以前的預定支出，都不可新增、修改或刪除。
/// 被擋下的寫入回 422 <c>locked</c>，資料與稽核記錄都不變。
/// </summary>
public class LockDateEndpointsTests(PostgresFixture postgres)
{
    private const string LockDate = "2026-01-31";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Set_lock_date_is_recorded_in_book_and_audit()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();

        await SetLockDateAsync(client, book, LockDate);

        (await GetBookAsync(client, book)).LockDate.Should().Be(new DateOnly(2026, 1, 31));
        var entry = (await AuditTrailTests.EntriesAsync(factory)).Should().ContainSingle().Subject;
        entry.Action.Should().Be(AuditAction.LockDateChanged);
        entry.EntityType.Should().Be("Book");
        entry.EntityId.Should().Be(book.Id.Value);
        entry.BookId.Should().Be(book.Id.Value);
        LockDateIn(entry.Before).Should().BeNull();
        LockDateIn(entry.After).Should().Be(LockDate);
    }

    [Fact]
    public async Task Clearing_lock_date_reopens_the_locked_period()
    {
        // 鎖帳日可以清除（spec §9 O3）；清除也是一次變更，留下稽核記錄。
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        await SetLockDateAsync(client, book, LockDate);

        await SetLockDateAsync(client, book, null);

        (await GetBookAsync(client, book)).LockDate.Should().BeNull();
        var entry = (await AuditTrailTests.EntriesAsync(factory)).Last();
        entry.Action.Should().Be(AuditAction.LockDateChanged);
        LockDateIn(entry.Before).Should().Be(LockDate);
        LockDateIn(entry.After).Should().BeNull();
        await AuditTrailTests.CreateTransactionAsync(client, book, Lunch(book, "2026-01-20"));
    }

    [Fact]
    public async Task Create_transaction_in_locked_period_is_422_locked()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        await SetLockDateAsync(client, book, LockDate);
        var baseline = await AuditTrailTests.EntriesAsync(factory);

        var response = await client.PostAsJsonAsync(TransactionsUrl(book), Lunch(book, LockDate), ApiJson.Options, Ct);

        await ShouldBeLockedAsync(response);
        (await ListTransactionsAsync(client, book)).Should().BeEmpty();
        (await AuditTrailTests.EntriesAsync(factory)).Should().HaveSameCount(baseline);
        // 鎖帳日的隔天仍然開放。
        await AuditTrailTests.CreateTransactionAsync(client, book, Lunch(book, "2026-02-01"));
    }

    [Fact]
    public async Task Moving_transaction_into_locked_period_is_422()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var created = await AuditTrailTests.CreateTransactionAsync(client, book, Lunch(book, "2026-02-05"));
        await SetLockDateAsync(client, book, LockDate);

        await ShouldRejectUpdateAsync(factory, client, book, created, Lunch(book, "2026-01-20"));
    }

    [Fact]
    public async Task Moving_transaction_out_of_locked_period_is_422()
    {
        // 交易要在設定鎖帳日之前建立；新日期是開放的，只有檢查「原日期」才擋得住。
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var created = await AuditTrailTests.CreateTransactionAsync(client, book, Lunch(book, "2026-01-20"));
        await SetLockDateAsync(client, book, LockDate);

        await ShouldRejectUpdateAsync(factory, client, book, created, Lunch(book, "2026-02-05"));
    }

    [Fact]
    public async Task Deleting_locked_transaction_is_422()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var created = await AuditTrailTests.CreateTransactionAsync(client, book, Lunch(book, "2026-01-20"));
        await SetLockDateAsync(client, book, LockDate);
        var baseline = await AuditTrailTests.EntriesAsync(factory);

        var response = await client.DeleteAsync($"{TransactionsUrl(book)}/{created.Id}?version={created.Version}", Ct);

        await ShouldBeLockedAsync(response);
        (await ListTransactionsAsync(client, book)).Should().ContainSingle().Which.Should().BeEquivalentTo(created);
        (await AuditTrailTests.EntriesAsync(factory)).Should().HaveSameCount(baseline);
    }

    [Fact]
    public async Task Deleting_payment_of_plan_in_locked_month_is_422()
    {
        // 刪除付款交易會讓預定支出回到未付（spec §9 O2），等於修改了已鎖月份的預定支出；即使交易本身的日期是開放的也要擋下。
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var planned = await PlannedExpensesEndpointsTests.CreateAsync(client, book, Insurance(book, 202601));
        var paid = await PayAsync(client, book, planned, "2026-02-05");
        await SetLockDateAsync(client, book, LockDate);
        var baseline = await AuditTrailTests.EntriesAsync(factory);

        var response = await client.DeleteAsync(
            $"{TransactionsUrl(book)}/{paid.Transaction.Id}?version={paid.Transaction.Version}", Ct);

        await ShouldBeLockedAsync(response);
        (await ListTransactionsAsync(client, book)).Should().ContainSingle().Which.Id.Should().Be(paid.Transaction.Id);
        (await GetPlannedAsync(client, book, planned.Id)).Should().BeEquivalentTo(paid.PlannedExpense);
        (await AuditTrailTests.EntriesAsync(factory)).Should().HaveSameCount(baseline);
    }

    [Fact]
    public async Task Planned_expense_in_locked_month_cannot_be_paid_or_changed()
    {
        // 預定支出歸屬 2026-01；新的月份與付款日期都是開放的，只有檢查「原本的歸屬月份」才擋得住。
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var planned = await PlannedExpensesEndpointsTests.CreateAsync(client, book, Insurance(book, 202601));
        await SetLockDateAsync(client, book, LockDate);
        var baseline = await AuditTrailTests.EntriesAsync(factory);
        var url = $"{PlannedExpensesEndpointsTests.Url(book)}/{planned.Id}";

        await ShouldBeLockedAsync(await client.PostAsJsonAsync($"{url}/pay",
            PayBody(book, planned, "2026-02-05"), ApiJson.Options, Ct));
        await ShouldBeLockedAsync(await client.PutAsJsonAsync(url,
            new { version = planned.Version, input = Insurance(book, 202602) }, ApiJson.Options, Ct));
        await ShouldBeLockedAsync(await client.DeleteAsync($"{url}?version={planned.Version}", Ct));
        await ShouldBeLockedAsync(await client.PostAsJsonAsync(PlannedExpensesEndpointsTests.Url(book),
            Insurance(book, 202601), ApiJson.Options, Ct));

        (await PlannedExpensesEndpointsTests.ListAsync(client, book, "")).Should().ContainSingle().Which.Should().BeEquivalentTo(planned);
        (await ListTransactionsAsync(client, book)).Should().BeEmpty();
        (await AuditTrailTests.EntriesAsync(factory)).Should().HaveSameCount(baseline);
    }

    [Fact]
    public async Task Moving_planned_expense_into_locked_month_is_422()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var planned = await PlannedExpensesEndpointsTests.CreateAsync(client, book, Insurance(book, 202602));
        await SetLockDateAsync(client, book, LockDate);
        var baseline = await AuditTrailTests.EntriesAsync(factory);

        var response = await client.PutAsJsonAsync($"{PlannedExpensesEndpointsTests.Url(book)}/{planned.Id}",
            new { version = planned.Version, input = Insurance(book, 202601) }, ApiJson.Options, Ct);

        await ShouldBeLockedAsync(response);
        (await GetPlannedAsync(client, book, planned.Id)).Should().BeEquivalentTo(planned);
        (await AuditTrailTests.EntriesAsync(factory)).Should().HaveSameCount(baseline);
    }

    [Fact]
    public async Task Paying_with_date_in_locked_period_is_422()
    {
        // 預定支出歸屬開放的 2026-02，但付款日期落在鎖帳日以前：付款會新增一筆鎖定期間的交易。
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var planned = await PlannedExpensesEndpointsTests.CreateAsync(client, book, Insurance(book, 202602));
        await SetLockDateAsync(client, book, LockDate);
        var baseline = await AuditTrailTests.EntriesAsync(factory);

        var response = await client.PostAsJsonAsync($"{PlannedExpensesEndpointsTests.Url(book)}/{planned.Id}/pay",
            PayBody(book, planned, "2026-01-20"), ApiJson.Options, Ct);

        await ShouldBeLockedAsync(response);
        (await GetPlannedAsync(client, book, planned.Id)).Should().BeEquivalentTo(planned);
        (await ListTransactionsAsync(client, book)).Should().BeEmpty();
        (await AuditTrailTests.EntriesAsync(factory)).Should().HaveSameCount(baseline);
    }

    private static async Task ShouldRejectUpdateAsync(ApiFactory factory, HttpClient client, Book book, TransactionDto created, object input)
    {
        var baseline = await AuditTrailTests.EntriesAsync(factory);

        var response = await client.PutAsJsonAsync($"{TransactionsUrl(book)}/{created.Id}",
            new { version = created.Version, input }, ApiJson.Options, Ct);

        await ShouldBeLockedAsync(response);
        (await ListTransactionsAsync(client, book)).Should().ContainSingle().Which.Should().BeEquivalentTo(created);
        (await AuditTrailTests.EntriesAsync(factory)).Should().HaveSameCount(baseline);
    }

    private static async Task SetLockDateAsync(HttpClient client, Book book, string? lockDate)
    {
        var response = await client.PutAsJsonAsync($"/api/books/{book.Id.Value}/lock-date", new { lockDate }, ApiJson.Options, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private static async Task<BookDto> GetBookAsync(HttpClient client, Book book) =>
        (await client.GetFromJsonAsync<BookDto>($"/api/books/{book.Id.Value}", ApiJson.Options, Ct))!;

    private static async Task ShouldBeLockedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().Should().Be("locked");
    }

    private static string TransactionsUrl(Book book) => $"/api/books/{book.Id.Value}/transactions";

    private static async Task<List<TransactionDto>> ListTransactionsAsync(HttpClient client, Book book) =>
        (await client.GetFromJsonAsync<List<TransactionDto>>(TransactionsUrl(book), ApiJson.Options, Ct))!;

    private static async Task<PlannedExpenseDto> GetPlannedAsync(HttpClient client, Book book, Guid id) =>
        (await client.GetFromJsonAsync<PlannedExpenseDto>($"{PlannedExpensesEndpointsTests.Url(book)}/{id}", ApiJson.Options, Ct))!;

    private static async Task<PayPlannedExpenseResult> PayAsync(HttpClient client, Book book, PlannedExpenseDto planned, string date)
    {
        var response = await client.PostAsJsonAsync($"{PlannedExpensesEndpointsTests.Url(book)}/{planned.Id}/pay",
            PayBody(book, planned, date), ApiJson.Options, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<PayPlannedExpenseResult>(ApiJson.Options, Ct))!;
    }

    private static object PayBody(Book book, PlannedExpenseDto planned, string date) =>
        new { version = planned.Version, date, accountId = book.FindAccount("國泰世華銀行")!.Id.Value, amount = -3000m };

    private static object Insurance(Book book, int budgetMonth) =>
        PlannedExpensesEndpointsTests.InsuranceInput(book, budgetMonth, -3000m, "保險費");

    private static object Lunch(Book book, string date) => new
    {
        kind = "Expense", date, amount = -120m, note = "午餐",
        accountId = book.FindAccount("現金")!.Id.Value, categoryId = book.FindCategory("主食", "午餐")!.Id.Value,
    };

    /// <summary>稽核快照 <c>{ lockDate }</c> 裡的鎖帳日；清除時為 null。</summary>
    private static string? LockDateIn(string? snapshot)
    {
        snapshot.Should().NotBeNull();
        return JsonDocument.Parse(snapshot!).RootElement.GetProperty("lockDate").GetString();
    }
}
