using FluentAssertions;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using Xunit;

namespace SixJars.Domain.Tests.Planning;

public class RecurringPlannedExpenseTests
{
    private readonly Book _book = new("測試帳本", new DateOnly(2025, 12, 30));
    private readonly Account _bank;
    private readonly Category _rent;
    private readonly Category _loan;

    public RecurringPlannedExpenseTests()
    {
        _bank = _book.AddAccount("銀行", AccountType.Bank);
        _rent = _book.AddExpenseCategory("房租", ExpenseNature.Fixed);
        _loan = _book.AddExpenseCategory("貸款支出", ExpenseNature.Loan);
        _book.AddExpenseCategory("飲食", ExpenseNature.Floating);
        _book.AddExpenseCategory("禮金", ExpenseNature.Special);
    }

    private static BudgetMonth M(int key) => BudgetMonth.FromKey(key);

    private RecurringPlannedExpense Monthly(BudgetMonth start, BudgetMonth? end = null) =>
        RecurringPlannedExpense.Create(_book, _rent.Id, _bank.Id, -15000m, "房租", RecurrenceFrequency.Monthly, [], start, end);

    [Fact]
    public void Monthly_is_due_in_every_month_of_the_closed_range()
    {
        var rent = Monthly(M(202602), M(202605));

        rent.IsDueIn(M(202601)).Should().BeFalse();
        rent.IsDueIn(M(202602)).Should().BeTrue();
        rent.IsDueIn(M(202605)).Should().BeTrue();
        rent.IsDueIn(M(202606)).Should().BeFalse();
    }

    [Fact]
    public void Without_end_month_it_is_due_forever_after_start()
    {
        Monthly(M(202602)).IsDueIn(M(203012)).Should().BeTrue();
    }

    [Fact]
    public void Yearly_is_due_only_in_listed_months_and_months_are_normalized()
    {
        var insurance = RecurringPlannedExpense.Create(
            _book, _rent.Id, null, -3000m, null, RecurrenceFrequency.Yearly, [7, 1, 7], M(202601), null);

        insurance.Months.Should().Equal(1, 7);   // 排序、去重
        insurance.IsDueIn(M(202601)).Should().BeTrue();
        insurance.IsDueIn(M(202602)).Should().BeFalse();
        insurance.IsDueIn(M(202707)).Should().BeTrue();
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 0 })]
    [InlineData(new[] { 13 })]
    public void Yearly_requires_at_least_one_month_between_1_and_12(int[] months)
    {
        var act = () => RecurringPlannedExpense.Create(
            _book, _rent.Id, null, -3000m, null, RecurrenceFrequency.Yearly, months, M(202601), null);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainException.RuleCode);
    }

    [Fact]
    public void Monthly_does_not_accept_months()
    {
        var act = () => RecurringPlannedExpense.Create(
            _book, _rent.Id, null, -3000m, null, RecurrenceFrequency.Monthly, [1], M(202601), null);

        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("飲食")]
    [InlineData("禮金")]
    public void Only_fixed_or_loan_categories_are_allowed(string name)
    {
        var category = _book.FindCategory(name)!;

        var act = () => RecurringPlannedExpense.Create(
            _book, category.Id, null, -100m, null, RecurrenceFrequency.Monthly, [], M(202601), null);

        act.Should().Throw<DomainException>().WithMessage($"*{name}*");
    }

    [Fact]
    public void Sub_category_of_a_fixed_main_category_is_allowed()
    {
        var insurance = _book.AddSubCategory(_rent.Id, "管理費");

        var act = () => RecurringPlannedExpense.Create(
            _book, insurance.Id, null, -100m, null, RecurrenceFrequency.Monthly, [], M(202601), null);

        act.Should().NotThrow();
    }

    [Fact]
    public void End_month_before_start_month_is_rejected()
    {
        var act = () => Monthly(M(202605), M(202604));

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Unknown_account_is_rejected()
    {
        var act = () => RecurringPlannedExpense.Create(
            _book, _rent.Id, AccountId.New(), -100m, null, RecurrenceFrequency.Monthly, [], M(202601), null);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Update_replaces_every_field_with_the_same_rules()
    {
        var item = Monthly(M(202601));

        item.Update(_book, _loan.Id, null, -20000m, "房貸", RecurrenceFrequency.Yearly, [3], M(202603), M(202612));

        item.CategoryId.Should().Be(_loan.Id);
        item.AccountId.Should().BeNull();
        item.DefaultAmount.Should().Be(-20000m);
        item.Note.Should().Be("房貸");
        item.Frequency.Should().Be(RecurrenceFrequency.Yearly);
        item.Months.Should().Equal(3);
        (item.StartMonth, item.EndMonth).Should().Be((M(202603), M(202612)));
    }

    [Fact]
    public void Failed_update_leaves_the_item_unchanged()
    {
        var item = Monthly(M(202601));
        var food = _book.FindCategory("飲食")!;

        var act = () => item.Update(_book, food.Id, null, -1m, "x", RecurrenceFrequency.Monthly, [], M(202601), null);

        act.Should().Throw<DomainException>();
        item.CategoryId.Should().Be(_rent.Id);
        item.DefaultAmount.Should().Be(-15000m);
    }

    [Fact]
    public void Cannot_be_removed_after_it_has_generated_planned_expenses()
    {
        var item = Monthly(M(202601));

        item.Invoking(i => i.EnsureRemovable(hasGenerated: false)).Should().NotThrow();
        item.Invoking(i => i.EnsureRemovable(hasGenerated: true))
            .Should().Throw<DomainException>().Which.Code.Should().Be(DomainException.InUseCode);
    }
}
