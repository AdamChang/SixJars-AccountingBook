using FluentAssertions;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using Xunit;

namespace SixJars.Domain.Tests.Planning;

public class CategoryBudgetTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 9, 1, 0, 0, TimeSpan.Zero);
    private static readonly BudgetMonth January = BudgetMonth.FromKey(202601);
    private static readonly BudgetMonth February = BudgetMonth.FromKey(202602);
    private readonly Book _book = new("測試帳本", new DateOnly(2025, 12, 30));
    private readonly Category _food;

    public CategoryBudgetTests()
    {
        _food = _book.AddExpenseCategory("飲食", ExpenseNature.Floating);
        _book.AddSubCategory(_food.Id, "午餐");
        _book.AddExpenseCategory("房租", ExpenseNature.Fixed);
        _book.AddIncomeCategory("薪資");
    }

    private CategoryBudget Food() => CategoryBudget.Create(_book, _food.Id);

    [Fact]
    public void New_budget_has_nothing_set()
    {
        var budget = Food();

        (budget.CategoryId, budget.BookId).Should().Be((_food.Id, _book.Id));
        budget.DefaultAmount.Should().BeNull();
        budget.Overrides.Should().BeEmpty();
        budget.IsEmpty.Should().BeTrue();
        budget.AmountFor(January).Should().Be(((decimal?)null, (BudgetSource?)null));
    }

    [Fact]
    public void Default_applies_to_every_month()
    {
        var budget = Food();

        budget.SetDefault(5000m);

        budget.AmountFor(January).Should().Be(((decimal?)5000m, (BudgetSource?)BudgetSource.Default));
        budget.AmountFor(BudgetMonth.FromKey(203012)).Should().Be(((decimal?)5000m, (BudgetSource?)BudgetSource.Default));
    }

    [Fact]
    public void Override_wins_only_in_its_month()
    {
        var budget = Food();
        budget.SetDefault(5000m);

        budget.SetOverride(February, 3000m);

        budget.AmountFor(February).Should().Be(((decimal?)3000m, (BudgetSource?)BudgetSource.Override));
        budget.AmountFor(January).Should().Be(((decimal?)5000m, (BudgetSource?)BudgetSource.Default));
        budget.HasOverride(February).Should().BeTrue();
        budget.HasOverride(January).Should().BeFalse();
    }

    [Fact]
    public void Setting_an_existing_override_changes_it_in_place()
    {
        var budget = Food();
        budget.SetOverride(January, 100m);
        var first = budget.Overrides.Single();

        budget.SetOverride(January, 200m);

        // 原地改金額，EF 才會發出 UPDATE 而不是 DELETE＋INSERT（L2 的假設 a）
        budget.Overrides.Should().ContainSingle().Which.Should().BeSameAs(first);
        first.Amount.Should().Be(200m);
    }

    [Fact]
    public void Only_override_leaves_other_months_unset()
    {
        var budget = Food();

        budget.SetOverride(January, 800m);

        budget.AmountFor(February).Should().Be(((decimal?)null, (BudgetSource?)null));
        budget.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Removing_default_keeps_overrides()
    {
        var budget = Food();
        budget.SetDefault(5000m);
        budget.SetOverride(January, 100m);

        budget.RemoveDefault().Should().BeTrue();

        budget.DefaultAmount.Should().BeNull();
        budget.AmountFor(January).Should().Be(((decimal?)100m, (BudgetSource?)BudgetSource.Override));
        budget.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Removing_values_that_do_not_exist_returns_false()
    {
        var budget = Food();
        budget.SetOverride(January, 100m);

        budget.RemoveDefault().Should().BeFalse();
        budget.RemoveOverride(February).Should().BeFalse();
        budget.Overrides.Should().ContainSingle();
    }

    [Fact]
    public void Budget_is_empty_after_removing_everything()
    {
        var budget = Food();
        budget.SetDefault(5000m);
        budget.SetOverride(January, 100m);

        budget.RemoveOverride(January).Should().BeTrue();
        budget.RemoveDefault().Should().BeTrue();

        budget.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Zero_is_a_valid_budget()
    {
        var budget = Food();

        budget.SetDefault(0m);
        budget.SetOverride(January, 0m);

        budget.AmountFor(January).Should().Be(((decimal?)0m, (BudgetSource?)BudgetSource.Override));
        budget.AmountFor(February).Should().Be(((decimal?)0m, (BudgetSource?)BudgetSource.Default));
    }

    [Fact]
    public void Negative_amounts_are_rejected_and_change_nothing()
    {
        var budget = Food();
        budget.SetDefault(5000m);

        budget.Invoking(b => b.SetDefault(-1m)).Should().Throw<DomainException>().Which.Code.Should().Be(DomainException.RuleCode);
        budget.Invoking(b => b.SetOverride(January, -0.01m)).Should().Throw<DomainException>();

        budget.DefaultAmount.Should().Be(5000m);
        budget.Overrides.Should().BeEmpty();
    }

    [Theory]
    [InlineData("房租")]
    [InlineData("午餐")]
    [InlineData("薪資")]
    public void Only_floating_expense_main_categories_can_have_budgets(string name)
    {
        var category = _book.Categories.Single(c => c.Name == name);

        var act = () => CategoryBudget.Create(_book, category.Id);

        act.Should().Throw<DomainException>().WithMessage($"*{name}*");
    }

    [Fact]
    public void Unknown_category_is_rejected()
    {
        var act = () => CategoryBudget.Create(_book, CategoryId.New());

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Archived_floating_category_can_still_have_a_budget()
    {
        _book.ArchiveCategory(_food.Id, At);

        // P4 L plan D4：後端不以封存為驗證條件
        var act = () => Food().SetDefault(100m);

        act.Should().NotThrow();
    }
}
