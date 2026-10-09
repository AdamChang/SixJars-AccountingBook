using FluentAssertions;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using Xunit;

namespace SixJars.Domain.Tests.Planning;

public class BudgetSheetTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 9, 1, 0, 0, TimeSpan.Zero);
    private static readonly BudgetMonth February = BudgetMonth.FromKey(202602);
    private readonly Book _book = new("測試帳本", new DateOnly(2025, 12, 30));
    private readonly Category _food;
    private readonly Category _lunch;
    private readonly Category _rent;
    private readonly Category _transport;

    public BudgetSheetTests()
    {
        _food = _book.AddExpenseCategory("飲食", ExpenseNature.Floating);
        _lunch = _book.AddSubCategory(_food.Id, "午餐");
        _rent = _book.AddExpenseCategory("房租", ExpenseNature.Fixed);
        _transport = _book.AddExpenseCategory("交通", ExpenseNature.Floating);
        _book.AddIncomeCategory("薪資");
    }

    private BudgetSheetResult Build(Dictionary<CategoryId, decimal>? expenseTotals = null, params CategoryBudget[] budgets) =>
        BudgetSheet.Build(_book, February, budgets, expenseTotals ?? []);

    private CategoryBudget Budget(Category category, decimal? defaultAmount = null, decimal? february = null)
    {
        var budget = CategoryBudget.Create(_book, category.Id);
        if (defaultAmount is { } amount)
        {
            budget.SetDefault(amount);
        }

        if (february is { } monthAmount)
        {
            budget.SetOverride(February, monthAmount);
        }

        return budget;
    }

    [Fact]
    public void Lists_floating_expense_main_categories_in_sort_order()
    {
        _book.ReorderCategories(CategoryKind.Expense, null, [_transport.Id, _rent.Id, _food.Id]);

        Build().Rows.Select(r => r.CategoryId).Should().Equal(_transport.Id, _food.Id);
    }

    [Fact]
    public void Actual_is_positive_and_includes_sub_categories()
    {
        var totals = new Dictionary<CategoryId, decimal> { [_food.Id] = -100m, [_lunch.Id] = -850.5m, [_rent.Id] = -15000m };

        Build(totals).Rows.Single(r => r.CategoryId == _food.Id).Actual.Should().Be(950.5m);
    }

    [Fact]
    public void Refunds_reduce_actual()
    {
        var totals = new Dictionary<CategoryId, decimal> { [_food.Id] = -300m, [_lunch.Id] = 50m };

        Build(totals).Rows.Single(r => r.CategoryId == _food.Id).Actual.Should().Be(250m);
    }

    [Fact]
    public void Override_wins_over_default_and_default_is_still_reported()
    {
        var totals = new Dictionary<CategoryId, decimal> { [_lunch.Id] = -1000m };

        var row = Build(totals, Budget(_food, 5000m, 3000m)).Rows.Single(r => r.CategoryId == _food.Id);

        row.Should().Be(new BudgetRow(_food.Id, 5000m, 3000m, BudgetSource.Override, 1000m, 2000m));
    }

    [Fact]
    public void Default_applies_when_the_month_has_no_override()
    {
        Build(null, Budget(_food, 5000m)).Rows.Single(r => r.CategoryId == _food.Id)
            .Should().Be(new BudgetRow(_food.Id, 5000m, 5000m, BudgetSource.Default, 0m, 5000m));
    }

    [Fact]
    public void Category_without_a_budget_for_the_month_has_no_budget_or_remaining()
    {
        var marchOnly = CategoryBudget.Create(_book, _transport.Id);
        marchOnly.SetOverride(BudgetMonth.FromKey(202603), 800m);
        var totals = new Dictionary<CategoryId, decimal> { [_transport.Id] = -200m };

        // P4 L plan Q1a：只有別月的覆寫值，本月「未設」，actual 照常列出
        Build(totals, marchOnly).Rows.Single(r => r.CategoryId == _transport.Id)
            .Should().Be(new BudgetRow(_transport.Id, null, null, null, 200m, null));
    }

    [Fact]
    public void Overspending_gives_negative_remaining()
    {
        var totals = new Dictionary<CategoryId, decimal> { [_food.Id] = -620m };

        Build(totals, Budget(_food, 500m)).Rows.Single(r => r.CategoryId == _food.Id).Remaining.Should().Be(-120m);
    }

    [Fact]
    public void Archived_main_category_is_listed_only_with_actual_or_override()
    {
        _book.ArchiveCategory(_transport.Id, At);

        Build(null, Budget(_transport, 800m)).Rows.Select(r => r.CategoryId).Should().Equal(_food.Id);
        Build(new Dictionary<CategoryId, decimal> { [_transport.Id] = -1m }).Rows.Select(r => r.CategoryId)
            .Should().Equal(_food.Id, _transport.Id);
        Build(null, Budget(_transport, february: 300m)).Rows.Select(r => r.CategoryId)
            .Should().Equal(_food.Id, _transport.Id);
    }

    [Fact]
    public void Totals_only_include_rows_with_a_budget()
    {
        var totals = new Dictionary<CategoryId, decimal> { [_food.Id] = -1000m, [_transport.Id] = -200m };

        // P4 L plan D6：交通沒有預算，它的 200 不算進已用
        Build(totals, Budget(_food, 3000m)).Totals.Should().Be(new BudgetTotals(3000m, 1000m, 2000m));
    }

    [Fact]
    public void Totals_are_zero_without_any_budget()
    {
        var totals = new Dictionary<CategoryId, decimal> { [_food.Id] = -1000m };

        Build(totals).Totals.Should().Be(new BudgetTotals(0m, 0m, 0m));
    }
}
