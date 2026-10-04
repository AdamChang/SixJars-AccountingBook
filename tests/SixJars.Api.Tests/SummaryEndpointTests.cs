using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Application.Ledger;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Ledger;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

public class SummaryEndpointTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Summary_matches_domain_calculators()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var other = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        await RecordLedgerAsync(client, book);
        // 別本帳的同月資料：不可混進來。
        await RecordLedgerAsync(client, other);
        await CreateTransactionAsync(client, other, new
        {
            kind = "Income", date = "2026-02-03", amount = 99999m,
            accountId = Account(other, "現金"), categoryId = other.FindCategory("工作薪資")!.Id.Value,
        });
        await PlannedExpensesEndpointsTests.CreateAsync(client, other, PlannedExpensesEndpointsTests.InsuranceInput(other, 202602, -7777m, "別人的"));

        var summary = await GetSummaryAsync(client, book, "?budgetMonth=202602&asOf=2026-02-20");

        var expected = await ExpectedAsync(factory, book, new BudgetMonth(2026, 2), new DateOnly(2026, 2, 20));
        summary.Should().BeEquivalentTo(expected, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Summary_without_as_of_uses_today_in_taipei()
    {
        // UTC 2/28 17:00 是台北的 3/1 01:00：若誤用 UTC 的日期，3/1 的交易就不會算進去。
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 2, 28, 17, 0, 0, TimeSpan.Zero));
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct, services => services.AddSingleton<TimeProvider>(clock));
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        await CreateTransactionAsync(client, book, new
        {
            kind = "Expense", date = "2026-03-01", amount = -100m,
            accountId = Account(book, "現金"), categoryId = book.FindCategory("主食", "午餐")!.Id.Value,
        });

        var summary = await GetSummaryAsync(client, book, "?budgetMonth=202603");

        summary.AsOf.Should().Be(new DateOnly(2026, 3, 1));
        summary.Accounts.Single(a => a.Name == "現金").Balance.Should().Be(900m);
        summary.Should().BeEquivalentTo(
            await ExpectedAsync(factory, book, new BudgetMonth(2026, 3), new DateOnly(2026, 3, 1)), o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Summary_uses_at_most_five_database_round_trips()
    {
        var counter = new CommandCounter();
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct, services => services.AddSingleton<IInterceptor>(counter));
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        await RecordLedgerAsync(client, book);

        counter.Reset();
        await GetSummaryAsync(client, book, "?budgetMonth=202602&asOf=2026-02-20");

        // Book 1 次 + 分錄 1 次 + 財務規劃帳戶 1 次 + 交易 1 次 + 預定支出 1 次（spec §5 原本預期 ≤ 3，見計畫 T27 的偏差說明）。
        counter.Count.Should().BeLessThanOrEqualTo(5);
    }

    [Theory]
    [InlineData("?budgetMonth=202600")]
    [InlineData("?budgetMonth=202613")]
    public async Task Invalid_budget_month_is_400(string query)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await factory.CreateSignedInClient().GetAsync($"/api/books/{book.Id.Value}/summary{query}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("errors").TryGetProperty("budgetMonth", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Summary_of_unknown_book_is_404()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var unknown = Guid.NewGuid();

        var response = await factory.CreateSignedInClient().GetAsync($"/api/books/{unknown}/summary?budgetMonth=202602", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        // 確認是 handler 找不到帳本，不是路由不存在（路由不存在也是 404，但沒有 ProblemDetails）。
        (await ReadProblemAsync(response)).GetProperty("detail").GetString().Should().Contain(unknown.ToString());
    }

    /// <summary>
    /// 1、2 月的交易，涵蓋月可用餘額的各種規則，加上一筆截止日之後的交易、一筆已付與一筆未付的預定支出。
    /// </summary>
    private static async Task RecordLedgerAsync(HttpClient client, Book book)
    {
        var cash = Account(book, "現金");
        var bank = Account(book, "國泰世華銀行");
        var card = Account(book, "國泰Combo卡");
        var wallet = Account(book, "悠遊卡");
        var loan = Account(book, "房屋貸款");
        var fund = book.FindPlanningFund("財務自由帳戶")!.Id.Value;
        var salary = book.FindCategory("工作薪資")!.Id.Value;
        var lunch = book.FindCategory("主食", "午餐")!.Id.Value;
        var mortgage = book.FindCategory("貸款支出", "房屋貸款")!.Id.Value;

        object[] inputs =
        [
            new { kind = "Income", date = "2026-01-05", amount = 50000m, accountId = bank, categoryId = salary },
            new { kind = "Expense", date = "2026-01-10", amount = -45m, accountId = wallet, categoryId = lunch },
            new { kind = "TopUp", date = "2026-01-12", amount = 200m, accountId = wallet, counterAccountId = cash },
            new { kind = "FundAllocation", date = "2026-01-15", amount = 3000m, accountId = bank, planningFundId = fund },
            new { kind = "Income", date = "2026-01-30", budgetMonth = 202602, amount = 60000m, accountId = bank, categoryId = salary },
            new { kind = "LoanPayment", date = "2026-02-05", amount = 20000m, loanPrincipal = 15000m, accountId = bank, counterAccountId = loan, categoryId = mortgage },
            new { kind = "Expense", date = "2026-02-10", amount = -800m, accountId = card, categoryId = lunch },
            new { kind = "Expense", date = "2026-02-11", amount = -60m, accountId = wallet, categoryId = lunch },
            new { kind = "TopUp", date = "2026-02-11", amount = 500m, accountId = wallet, counterAccountId = card },
            new { kind = "FundWithdrawal", date = "2026-02-12", amount = 1000m, accountId = bank, planningFundId = fund },
            // 截止日 2/20 之後：餘額不含，月可用餘額（依歸屬月份）要含。
            new { kind = "Expense", date = "2026-02-25", amount = -100m, accountId = cash, categoryId = lunch },
        ];
        foreach (var input in inputs)
        {
            await CreateTransactionAsync(client, book, input);
        }

        await PlannedExpensesEndpointsTests.CreateAsync(client, book, PlannedExpensesEndpointsTests.InsuranceInput(book, 202602, -3000m, "未付"));
        var toPay = await PlannedExpensesEndpointsTests.CreateAsync(client, book, PlannedExpensesEndpointsTests.InsuranceInput(book, 202602, -500m, "已付"));
        var paid = await client.PostAsJsonAsync($"{PlannedExpensesEndpointsTests.Url(book)}/{toPay.Id}/pay",
            new { version = toPay.Version, date = "2026-02-18", accountId = bank, amount = -480m }, ApiJson.Options, Ct);
        paid.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>Oracle：從 DB 載入快照，交給 P1 計算器。</summary>
    private static async Task<LedgerSummaryDto> ExpectedAsync(ApiFactory factory, Book book, BudgetMonth month, DateOnly asOf)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var ledger = await new LedgerSnapshotLoader(scope.ServiceProvider.GetRequiredService<SixJarsDbContext>()).LoadAsync(book.Id, Ct);
        var balances = new BalanceCalculator(ledger);
        var disposable = new DisposableBalanceCalculator(ledger);
        var cash = new AvailableCashCalculator(ledger);
        return new LedgerSummaryDto(
            month.Key,
            asOf,
            disposable.Monthly(month),
            disposable.YearToDate(month),
            cash.AsOf(asOf, includeEWallets: false),
            cash.AsOf(asOf, includeEWallets: true),
            [.. ledger.Book.Accounts.Select(a => new BalanceDto(a.Id.Value, a.Name, balances.AccountBalanceAsOf(a.Id, asOf)))],
            [.. ledger.Book.PlanningFunds.Select(f => new BalanceDto(f.Id.Value, f.Name, balances.FundBalanceAsOf(f.Id, asOf)))]);
    }

    private static Guid Account(Book book, string name) => book.FindAccount(name)!.Id.Value;

    private static async Task CreateTransactionAsync(HttpClient client, Book book, object input)
    {
        var response = await client.PostAsJsonAsync($"/api/books/{book.Id.Value}/transactions", input, ApiJson.Options, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<LedgerSummaryDto> GetSummaryAsync(HttpClient client, Book book, string query)
    {
        var response = await client.GetAsync($"/api/books/{book.Id.Value}/summary{query}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<LedgerSummaryDto>(ApiJson.Options, Ct))!;
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }
}
