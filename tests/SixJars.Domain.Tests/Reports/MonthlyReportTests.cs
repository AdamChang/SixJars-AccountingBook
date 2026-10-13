using FluentAssertions;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using SixJars.Domain.Reports;
using SixJars.Domain.Transactions;
using Xunit;

namespace SixJars.Domain.Tests.Reports;

public class MonthlyReportTests
{
    private static readonly BudgetMonth February = BudgetMonth.FromKey(202602);
    private readonly Book _book = new("測試帳本", new DateOnly(2025, 12, 30));
    private readonly Category _salary;
    private readonly Category _otherIncome;
    private readonly Category _bonus;
    private readonly Category _food;
    private readonly Category _lunch;
    private readonly Category _dinner;
    private readonly Category _transport;
    private readonly Category _fixed;
    private readonly Category _insurance;
    private readonly Category _loanCategory;
    private readonly Category _special;
    private readonly Account _mortgage;
    private readonly Account _carLoan;
    private readonly PlanningFund _freedom;
    private readonly PlanningFund _travel;

    public MonthlyReportTests()
    {
        _salary = _book.AddIncomeCategory("薪資");
        _otherIncome = _book.AddIncomeCategory("其它收入");
        _bonus = _book.AddSubCategory(_otherIncome.Id, "獎金");
        _food = _book.AddExpenseCategory("飲食", ExpenseNature.Floating);
        _lunch = _book.AddSubCategory(_food.Id, "午餐");
        _dinner = _book.AddSubCategory(_food.Id, "晚餐");
        _transport = _book.AddExpenseCategory("交通", ExpenseNature.Floating);
        _fixed = _book.AddExpenseCategory("固定支出", ExpenseNature.Fixed);
        _insurance = _book.AddSubCategory(_fixed.Id, "保險費");
        _loanCategory = _book.AddExpenseCategory("貸款支出", ExpenseNature.Loan);
        _special = _book.AddExpenseCategory("特別支出", ExpenseNature.Special);
        _mortgage = _book.AddAccount("房貸", AccountType.Loan);
        _carLoan = _book.AddAccount("車貸", AccountType.Loan);
        _freedom = _book.AddPlanningFund("財務自由");
        _travel = _book.AddPlanningFund("旅遊");
    }

    private static ReportFlow Flow(
        TransactionKind kind, decimal amount, Category? category = null, Account? counter = null, PlanningFund? fund = null,
        decimal principal = 0m, BudgetMonth? month = null) =>
        new(month ?? February, kind, category?.Id, counter?.Id, fund?.Id, amount, principal);

    private MonthlyReportResult Build(IEnumerable<ReportFlow> flows, Dictionary<CategoryId, decimal>? unpaid = null) =>
        MonthlyReport.Build(_book, February, flows, unpaid ?? []);

    private static MonthlyReportSection Section(MonthlyReportResult report, ReportSectionKind kind) =>
        report.Expenses.Single(s => s.Kind == kind);

    [Fact]
    public void Expense_lines_group_by_main_then_sub_with_unassigned_and_refunds()
    {
        var report = Build([
            Flow(TransactionKind.Expense, -100m, _lunch),
            Flow(TransactionKind.Expense, 20m, _lunch),        // 退款抵銷，不是取絕對值
            Flow(TransactionKind.Expense, -300m, _dinner),
            Flow(TransactionKind.Expense, -50m, _food),        // 直接掛在主分類
            Flow(TransactionKind.Expense, -70m, _transport),   // 沒有子分類的主分類：不輸出子列（D6）
        ]);

        var floating = Section(report, ReportSectionKind.Floating);
        floating.Total.Should().Be(500m);
        floating.Unpaid.Should().BeNull();
        floating.Lines.Should().BeEquivalentTo(
            new[]
            {
                new MonthlyReportLine(ReportLineKind.Category, _food.Id.Value, "飲食", 430m, null, null,
                [
                    new MonthlyReportLine(ReportLineKind.Category, _lunch.Id.Value, "午餐", 80m, null, null, []),
                    new MonthlyReportLine(ReportLineKind.Category, _dinner.Id.Value, "晚餐", 300m, null, null, []),
                    new MonthlyReportLine(ReportLineKind.Unassigned, _food.Id.Value, "（未分子類）", 50m, null, null, []),
                ]),
                new MonthlyReportLine(ReportLineKind.Category, _transport.Id.Value, "交通", 70m, null, null, []),
            },
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void Empty_lines_are_omitted_and_every_section_is_present()
    {
        var report = Build([Flow(TransactionKind.Expense, -10m, _lunch), Flow(TransactionKind.Expense, 10m, _lunch)]);

        report.Income.Lines.Should().BeEmpty();
        report.Expenses.Select(s => s.Kind).Should().Equal(
            ReportSectionKind.Floating, ReportSectionKind.Fixed, ReportSectionKind.Loan, ReportSectionKind.Special);
        report.Expenses.Should().OnlyContain(s => s.Total == 0m && s.Lines.Count == 0);
        report.ExpenseTotal.Should().Be(0m);
    }

    [Fact]
    public void Income_keeps_its_sign_and_groups_like_expenses()
    {
        var report = Build([
            Flow(TransactionKind.Income, 50000m, _salary),
            Flow(TransactionKind.Income, 3000m, _bonus),
            Flow(TransactionKind.Income, 200m, _otherIncome),
        ]);

        report.Income.Kind.Should().Be(ReportSectionKind.Income);
        report.Income.Total.Should().Be(53200m);
        report.Income.Unpaid.Should().BeNull();
        report.Income.Lines.Select(l => (l.Name, l.Amount)).Should().Equal(("薪資", 50000m), ("其它收入", 3200m));
        report.Income.Lines[1].Children.Select(c => (c.Kind, c.Name, c.Amount))
            .Should().Equal((ReportLineKind.Category, "獎金", 3000m), (ReportLineKind.Unassigned, "（未分子類）", 200m));
    }

    [Fact]
    public void Loan_payments_are_listed_by_loan_account_and_ignore_their_interest_category()
    {
        var report = Build([
            Flow(TransactionKind.LoanPayment, 20000m, _food, _mortgage, principal: 15000m),   // 利息分類是浮動的「飲食」（Q-A）
            Flow(TransactionKind.LoanPayment, 5000m, counter: _carLoan, principal: 5000m),    // 提前還本，利息 0
            Flow(TransactionKind.Expense, -800m, _loanCategory),                               // 貸款性質分類上的一般支出（D7）
        ]);

        Section(report, ReportSectionKind.Floating).Lines.Should().BeEmpty();
        var loan = Section(report, ReportSectionKind.Loan);
        loan.Total.Should().Be(25800m);
        loan.Lines.Select(l => (l.Kind, l.Name, l.Amount, l.Principal, l.Interest)).Should().Equal(
            (ReportLineKind.LoanAccount, "房貸", 20000m, (decimal?)15000m, (decimal?)5000m),
            (ReportLineKind.LoanAccount, "車貸", 5000m, (decimal?)5000m, (decimal?)0m),
            (ReportLineKind.Category, "貸款支出", 800m, (decimal?)null, (decimal?)null));
    }

    [Fact]
    public void Planning_lists_allocation_withdrawal_and_return_per_fund_outside_expense_totals()
    {
        var report = Build([
            Flow(TransactionKind.FundAllocation, 10000m, fund: _freedom),
            Flow(TransactionKind.FundWithdrawal, 3000m, _transport, fund: _travel),   // 帶浮動分類也不計入（Q-A）
            Flow(TransactionKind.FundReturn, 120m, fund: _freedom),
        ]);

        report.Planning.Should().Equal(
            new MonthlyPlanningLine(_freedom.Id.Value, "財務自由", 10000m, 0m, 120m),
            new MonthlyPlanningLine(_travel.Id.Value, "旅遊", 0m, 3000m, 0m));
        Section(report, ReportSectionKind.Floating).Lines.Should().BeEmpty();
        report.ExpenseTotal.Should().Be(0m);
    }

    [Fact]
    public void Unpaid_planned_expenses_are_subtotalled_per_nature_and_not_added_to_totals()
    {
        var report = Build(
            [Flow(TransactionKind.Expense, -1000m, _insurance)],
            new Dictionary<CategoryId, decimal>
            {
                [_insurance.Id] = -2000m, [_fixed.Id] = -500m, [_loanCategory.Id] = -15000m, [_special.Id] = -800m,
            });

        Section(report, ReportSectionKind.Fixed).Total.Should().Be(1000m);
        Section(report, ReportSectionKind.Fixed).Unpaid.Should().Be(2500m);   // 子分類依主分類的性質歸區
        Section(report, ReportSectionKind.Loan).Unpaid.Should().Be(15000m);
        Section(report, ReportSectionKind.Special).Unpaid.Should().Be(800m);  // Q17'
        Section(report, ReportSectionKind.Floating).Unpaid.Should().BeNull();
        report.ExpenseTotal.Should().Be(1000m);
        report.UnpaidTotal.Should().Be(18300m);
    }

    [Fact]
    public void Sections_without_unpaid_report_zero()
    {
        var report = Build([]);

        Section(report, ReportSectionKind.Fixed).Unpaid.Should().Be(0m);
        report.UnpaidTotal.Should().Be(0m);
    }

    [Fact]
    public void Flows_of_other_months_are_ignored()
    {
        var report = Build([Flow(TransactionKind.Expense, -100m, _lunch, month: BudgetMonth.FromKey(202603))]);

        report.ExpenseTotal.Should().Be(0m);
    }

    [Fact]
    public void Changed_nature_applies_retroactively()
    {
        _book.ChangeExpenseNature(_transport.Id, ExpenseNature.Fixed, hasPlannedExpenses: false);

        var report = Build([Flow(TransactionKind.Expense, -70m, _transport)]);

        Section(report, ReportSectionKind.Fixed).Lines.Select(l => l.Name).Should().Equal("交通");
        Section(report, ReportSectionKind.Floating).Lines.Should().BeEmpty();
    }

    [Fact]
    public void Floating_main_amounts_equal_budget_actuals()
    {
        ReportFlow[] flows =
        [
            Flow(TransactionKind.Expense, -100m, _lunch),
            Flow(TransactionKind.Expense, -50m, _food),
            Flow(TransactionKind.Expense, -70m, _transport),
            Flow(TransactionKind.Expense, 5m, _dinner),
            Flow(TransactionKind.LoanPayment, 20000m, _food, _mortgage, principal: 15000m),
            Flow(TransactionKind.FundWithdrawal, 3000m, _transport, fund: _travel),
        ];
        // 與 ExpenseTotalsByCategoryAsync 同一個口徑：只取 Expense，依分類加總（L plan D3、Q-A）
        var expenseTotals = flows.Where(f => f.Kind == TransactionKind.Expense)
            .GroupBy(f => f.CategoryId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(f => f.Amount));

        var report = Build(flows);
        var budget = BudgetSheet.Build(_book, February, [], expenseTotals);

        Section(report, ReportSectionKind.Floating).Lines.Select(l => (l.Id, l.Amount))
            .Should().Equal(budget.Rows.Where(r => r.Actual != 0m).Select(r => (r.CategoryId.Value, r.Actual)));
    }
}
