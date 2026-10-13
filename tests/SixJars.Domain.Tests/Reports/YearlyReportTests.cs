using FluentAssertions;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Reports;
using SixJars.Domain.Transactions;
using Xunit;

namespace SixJars.Domain.Tests.Reports;

public class YearlyReportTests
{
    private static readonly BudgetMonth PastYearToday = BudgetMonth.FromKey(202703);
    private readonly Book _book = new("測試帳本", new DateOnly(2025, 12, 30));
    private readonly Category _salary;
    private readonly Category _food;
    private readonly Category _lunch;
    private readonly Account _mortgage;
    private readonly PlanningFund _freedom;

    public YearlyReportTests()
    {
        _salary = _book.AddIncomeCategory("薪資");
        _food = _book.AddExpenseCategory("飲食", ExpenseNature.Floating);
        _lunch = _book.AddSubCategory(_food.Id, "午餐");
        _mortgage = _book.AddAccount("房貸", AccountType.Loan);
        _freedom = _book.AddPlanningFund("財務自由");
    }

    private static ReportFlow Flow(
        int monthKey, TransactionKind kind, decimal amount, Category? category = null, Account? counter = null, PlanningFund? fund = null,
        decimal principal = 0m) =>
        new(BudgetMonth.FromKey(monthKey), kind, category?.Id, counter?.Id, fund?.Id, amount, principal);

    private static decimal[] Months(params (int Month, decimal Amount)[] values)
    {
        var months = new decimal[12];
        foreach (var (month, amount) in values)
        {
            months[month - 1] = amount;
        }

        return months;
    }

    private ReportFlow[] Year2026() =>
    [
        Flow(202601, TransactionKind.Income, 40000m, _salary),
        Flow(202603, TransactionKind.Income, 60000m, _salary),
        Flow(202601, TransactionKind.Expense, -1000m, _lunch),
        Flow(202603, TransactionKind.Expense, -2000m, _lunch),
        Flow(202512, TransactionKind.Expense, -999m, _lunch),   // 前一年的 12 月不算
        Flow(202701, TransactionKind.Expense, -5m, _lunch),
    ];

    [Fact]
    public void Months_are_placed_by_budget_month_with_totals_share_and_average()
    {
        var report = YearlyReport.Build(_book, 2026, Year2026(), PastYearToday);

        report.Year.Should().Be(2026);
        report.AverageDivisor.Should().Be(12);
        report.Income.Figures.Should().BeEquivalentTo(
            new YearlyFigures(Months((1, 40000m), (3, 60000m)), 100000m, 1m, 8333.33m), o => o.WithStrictOrdering());
        var floating = report.Expenses.Single(s => s.Kind == ReportSectionKind.Floating);
        floating.Figures.Should().BeEquivalentTo(
            new YearlyFigures(Months((1, 1000m), (3, 2000m)), 3000m, 0.03m, 250m), o => o.WithStrictOrdering());
        // 有子分類列就輸出子列；沒有直接掛在主分類的金額，所以沒有「（未分子類）」（D6）
        floating.Lines.Single().Children.Select(c => (c.Kind, c.Name)).Should().Equal((ReportLineKind.Category, "午餐"));
        report.ExpenseTotal.Months.Should().Equal(Months((1, 1000m), (3, 2000m)));
        report.ExpenseTotal.Total.Should().Be(3000m);
    }

    [Theory]
    [InlineData(202703, 12)]   // 過去的年份
    [InlineData(202603, 3)]    // 今年：到本月為止
    [InlineData(202512, null)] // 未來的年份
    public void Average_divisor_depends_on_today(int todayKey, int? divisor)
    {
        var report = YearlyReport.Build(_book, 2026, Year2026(), BudgetMonth.FromKey(todayKey));

        report.AverageDivisor.Should().Be(divisor);
        report.Income.Figures.MonthlyAverage.Should().Be(
            divisor is { } d ? Math.Round(100000m / d, 2, MidpointRounding.AwayFromZero) : null);
    }

    [Fact]
    public void Share_of_income_is_null_without_income()
    {
        var report = YearlyReport.Build(_book, 2026, [Flow(202602, TransactionKind.Expense, -100m, _food)], PastYearToday);

        report.Expenses.Single(s => s.Kind == ReportSectionKind.Floating).Figures.ShareOfIncome.Should().BeNull();
    }

    [Fact]
    public void Loan_payments_use_loan_accounts_and_planning_is_split_by_direction()
    {
        var report = YearlyReport.Build(_book, 2026,
        [
            Flow(202602, TransactionKind.LoanPayment, 20000m, _food, _mortgage, principal: 15000m),
            Flow(202602, TransactionKind.FundAllocation, 10000m, fund: _freedom),
            Flow(202604, TransactionKind.FundWithdrawal, 3000m, _food, fund: _freedom),
            Flow(202605, TransactionKind.FundReturn, 50m, fund: _freedom),
        ], PastYearToday);

        report.Expenses.Single(s => s.Kind == ReportSectionKind.Floating).Lines.Should().BeEmpty();
        var loan = report.Expenses.Single(s => s.Kind == ReportSectionKind.Loan).Lines.Single();
        (loan.Kind, loan.Name, loan.Figures.Total).Should().Be((ReportLineKind.LoanAccount, "房貸", 20000m));
        report.Planning.Allocation.Single().Kind.Should().Be(ReportLineKind.PlanningFund);
        report.Planning.Allocation.Single().Figures.Months.Should().Equal(Months((2, 10000m)));
        report.Planning.Withdrawal.Single().Figures.Months.Should().Equal(Months((4, 3000m)));
        report.Planning.Return.Single().Figures.Months.Should().Equal(Months((5, 50m)));
    }
}
