using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Application.Reports;
using SixJars.Domain.Books;
using SixJars.Domain.Reports;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

/// <summary>報表（P4 段 M）：月報（M4）與年報（M5）。</summary>
public class ReportsEndpointsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Monthly_report_groups_spending_lists_unpaid_and_matches_summary_disposable()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var food = Category(book, "主食");
        var lunch = Category(book, "主食", "午餐");
        await PostTransactionAsync(client, book, new { kind = "Income", date = "2026-02-05", amount = 50000m, accountId = Account(book, "國泰世華銀行"), categoryId = Category(book, "工作薪資") });
        await PostTransactionAsync(client, book, new { kind = "Expense", date = "2026-02-11", amount = -60m, accountId = Account(book, "悠遊卡"), categoryId = lunch });
        await PostTransactionAsync(client, book, new { kind = "Expense", date = "2026-02-12", amount = -100m, accountId = Account(book, "現金"), categoryId = food });
        // 利息分類選了浮動的「主食」：依貸款帳戶列在貸款區，不進浮動（Q-A）
        await PostTransactionAsync(client, book, new
        {
            kind = "LoanPayment", date = "2026-02-15", amount = 20000m, accountId = Account(book, "國泰世華銀行"),
            counterAccountId = Account(book, "房屋貸款"), loanPrincipal = 15000m, categoryId = food,
        });
        await PostTransactionAsync(client, book, new
        {
            kind = "FundAllocation", date = "2026-02-01", amount = 3000m, accountId = Account(book, "國泰世華銀行"),
            planningFundId = book.FindPlanningFund("財務自由帳戶")!.Id.Value,
        });
        (await client.PostAsJsonAsync($"/api/books/{book.Id.Value}/planned-expenses",
            new { budgetMonth = 202602, categoryId = Category(book, "固定支出", "保險費"), estimatedAmount = -2000m }, ApiJson.Options, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var report = await GetMonthlyAsync(client, book, 202602);
        var summary = await client.GetFromJsonAsync<JsonElement>($"/api/books/{book.Id.Value}/summary?budgetMonth=202602", ApiJson.Options, Ct);

        report.BudgetMonth.Should().Be(202602);
        report.MonthlyDisposable.Should().Be(summary.GetProperty("monthlyDisposable").GetDecimal());
        report.Income.Total.Should().Be(50000m);
        var floating = report.Expenses.Single(s => s.Kind == ReportSectionKind.Floating);
        floating.Total.Should().Be(160m);
        floating.Lines.Single().Children.Select(c => (c.Kind, c.Name, c.Amount)).Should().Equal(
            (ReportLineKind.Category, "午餐", 60m), (ReportLineKind.Unassigned, "（未分子類）", 100m));
        report.Expenses.Single(s => s.Kind == ReportSectionKind.Loan).Lines.Single()
            .Should().Match<ReportLineDto>(l => l.Kind == ReportLineKind.LoanAccount && l.Name == "房屋貸款"
                && l.Amount == 20000m && l.Principal == 15000m && l.Interest == 5000m);
        report.Expenses.Single(s => s.Kind == ReportSectionKind.Fixed).Unpaid.Should().Be(2000m);
        report.ExpenseTotal.Should().Be(20160m);
        report.UnpaidTotal.Should().Be(2000m);
        report.Planning.Should().Equal(new PlanningLineDto(book.FindPlanningFund("財務自由帳戶")!.Id.Value, "財務自由帳戶", 3000m, 0m, 0m));
    }

    [Fact]
    public async Task Monthly_report_uses_at_most_six_database_round_trips()
    {
        var counter = new CommandCounter();
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct, services => services.AddSingleton<IInterceptor>(counter));
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        await PostTransactionAsync(client, book, new { kind = "Expense", date = "2026-02-11", amount = -60m, accountId = Account(book, "現金"), categoryId = Category(book, "主食") });

        counter.Reset();
        await GetMonthlyAsync(client, book, 202602);

        // 成員授權 1 + 帳本 1 + report flows 1 + 未付預定 1 + 月可用餘額 2（交易與預定支出）（D3）
        counter.Count.Should().BeLessThanOrEqualTo(6);
    }

    [Theory]
    [InlineData("?budgetMonth=202600")]
    [InlineData("?budgetMonth=202613")]
    public async Task Invalid_budget_month_is_400(string query)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await (await factory.CreateMemberClientAsync()).GetAsync($"/api/books/{book.Id.Value}/reports/monthly{query}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SettingsMaintenanceEndpointsTests.ReadProblemAsync(response)).GetProperty("errors").TryGetProperty("budgetMonth", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Yearly_report_places_months_and_uses_taipei_today_for_the_average()
    {
        // UTC 2026-03-31 16:30 = 台北 2026-04-01 00:30：今年的分母是 4（D9、spec Q10）
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct,
            services => services.AddSingleton<TimeProvider>(new FixedTimeProvider(new DateTimeOffset(2026, 3, 31, 16, 30, 0, TimeSpan.Zero))));
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var salary = Category(book, "工作薪資");
        var bank = Account(book, "國泰世華銀行");
        await PostTransactionAsync(client, book, new { kind = "Income", date = "2026-01-05", amount = 40000m, accountId = bank, categoryId = salary });
        await PostTransactionAsync(client, book, new { kind = "Income", date = "2026-03-05", amount = 60000m, accountId = bank, categoryId = salary });
        await PostTransactionAsync(client, book, new { kind = "Expense", date = "2026-03-06", amount = -1000m, accountId = bank, categoryId = Category(book, "主食") });

        var report = await GetYearlyAsync(client, book, 2026);

        report.Year.Should().Be(2026);
        report.AverageDivisor.Should().Be(4);
        report.AverageFromMonth.Should().Be(1);
        report.Income.Figures.Months.Should().Equal(40000m, 0m, 60000m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m);
        report.Income.Figures.MonthlyAverage.Should().Be(25000m);
        report.ExpenseTotal.Total.Should().Be(1000m);
        report.ExpenseTotal.ShareOfIncome.Should().Be(0.01m);
    }

    [Fact]
    public async Task Yearly_report_uses_at_most_four_database_round_trips()
    {
        var counter = new CommandCounter();
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct, services => services.AddSingleton<IInterceptor>(counter));
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        await PostTransactionAsync(client, book, new { kind = "Expense", date = "2026-02-11", amount = -60m, accountId = Account(book, "現金"), categoryId = Category(book, "主食") });

        counter.Reset();
        await GetYearlyAsync(client, book, 2026);

        // 成員授權 1 + 帳本 1 + report flows 1（D4）
        counter.Count.Should().BeLessThanOrEqualTo(4);
    }

    [Theory]
    [InlineData("?year=0")]
    [InlineData("?year=10000")]
    public async Task Invalid_year_is_400(string query)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await (await factory.CreateMemberClientAsync()).GetAsync($"/api/books/{book.Id.Value}/reports/yearly{query}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SettingsMaintenanceEndpointsTests.ReadProblemAsync(response)).GetProperty("errors").TryGetProperty("year", out _).Should().BeTrue();
    }

    private static async Task<YearlyReportDto> GetYearlyAsync(HttpClient client, Book book, int year)
    {
        var response = await client.GetAsync($"/api/books/{book.Id.Value}/reports/yearly?year={year}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<YearlyReportDto>(ApiJson.Options, Ct))!;
    }

    internal static Guid Account(Book book, string name) => book.FindAccount(name)!.Id.Value;

    internal static Guid Category(Book book, string main, string? sub = null) => book.FindCategory(main, sub)!.Id.Value;

    internal static async Task PostTransactionAsync(HttpClient client, Book book, object input)
    {
        var response = await client.PostAsJsonAsync($"/api/books/{book.Id.Value}/transactions", input, ApiJson.Options, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<MonthlyReportDto> GetMonthlyAsync(HttpClient client, Book book, int budgetMonth)
    {
        var response = await client.GetAsync($"/api/books/{book.Id.Value}/reports/monthly?budgetMonth={budgetMonth}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<MonthlyReportDto>(ApiJson.Options, Ct))!;
    }
}
