using FluentAssertions;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using SixJars.Domain.Transactions;
using Xunit;

namespace SixJars.Domain.Tests.Planning;

public class RecurringPlannerTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 7, 1, 0, 0, TimeSpan.Zero);
    private static readonly BudgetMonth April = BudgetMonth.FromKey(202604);
    private readonly Book _book = new("測試帳本", new DateOnly(2025, 12, 30));
    private readonly Account _bank;
    private readonly Category _fixed;
    private readonly Category _insurance;

    public RecurringPlannerTests()
    {
        _bank = _book.AddAccount("銀行", AccountType.Bank);
        _fixed = _book.AddExpenseCategory("固定支出", ExpenseNature.Fixed);
        _insurance = _book.AddSubCategory(_fixed.Id, "保險費");
    }

    private RecurringPlannedExpense Item(CategoryId? category = null, AccountId? account = null, decimal amount = -1000m,
        RecurrenceFrequency frequency = RecurrenceFrequency.Monthly, int[]? months = null) =>
        RecurringPlannedExpense.Create(_book, category ?? _insurance.Id, account, amount, "保險", frequency, months ?? [],
            BudgetMonth.FromKey(202601), null);

    [Fact]
    public void Generate_creates_due_items_with_source_and_skips_items_not_due()
    {
        var monthly = Item(account: _bank.Id);
        var july = Item(frequency: RecurrenceFrequency.Yearly, months: [7]);

        var result = RecurringPlanner.Generate(_book, April, [monthly, july], alreadyGenerated: new HashSet<RecurringPlannedExpenseId>());

        var created = result.Created.Should().ContainSingle().Subject;
        created.SourceId.Should().Be(monthly.Id);
        (created.BudgetMonth, created.CategoryId, created.AccountId, created.EstimatedAmount, created.Note)
            .Should().Be((April, _insurance.Id, (AccountId?)_bank.Id, -1000m, "保險"));
        result.Skipped.Should().BeEmpty();   // 7 月才適用的不列出
    }

    [Fact]
    public void Generate_reports_already_generated_before_archived()
    {
        var item = Item();
        _book.ArchiveCategory(_insurance.Id, At);

        var result = RecurringPlanner.Generate(_book, April, [item], new HashSet<RecurringPlannedExpenseId> { item.Id });

        result.Created.Should().BeEmpty();
        result.Skipped.Should().Equal(new RecurringSkip(item.Id, null, RecurringSkipReason.AlreadyGenerated));
    }

    [Fact]
    public void Generate_skips_when_the_main_category_is_archived()
    {
        var item = Item();
        _book.ArchiveCategory(_fixed.Id, At);   // 封存主分類，子分類本身未封存

        var result = RecurringPlanner.Generate(_book, April, [item], new HashSet<RecurringPlannedExpenseId>());

        result.Skipped.Should().Equal(new RecurringSkip(item.Id, null, RecurringSkipReason.CategoryArchived));
    }

    [Fact]
    public void Generate_skips_when_the_account_is_archived()
    {
        var item = Item(account: _bank.Id);
        _book.ArchiveAccount(_bank.Id, balance: 0m, At);

        var result = RecurringPlanner.Generate(_book, April, [item], new HashSet<RecurringPlannedExpenseId>());

        result.Skipped.Should().Equal(new RecurringSkip(item.Id, null, RecurringSkipReason.AccountArchived));
    }

    [Fact]
    public void Refresh_updates_unpaid_generated_items_to_current_values_and_reports_only_changes()
    {
        var changed = Item(amount: -1000m);
        var unchanged = Item(amount: -500m);
        var planned = Generated(changed, unchanged);
        changed.Update(_book, _insurance.Id, _bank.Id, -1200m, "保費調漲", RecurrenceFrequency.Monthly, [], BudgetMonth.FromKey(202601), null);

        var result = RecurringPlanner.Refresh(_book, April, [changed, unchanged], planned);

        var updated = result.Updated.Should().ContainSingle().Subject;
        updated.SourceId.Should().Be(changed.Id);
        (updated.AccountId, updated.EstimatedAmount, updated.Note).Should().Be(((AccountId?)_bank.Id, -1200m, "保費調漲"));
        result.Skipped.Should().BeEmpty();
    }

    [Fact]
    public void Refresh_ignores_paid_deleted_and_manual_items()
    {
        var item = Item();
        var paid = Generated(item).Single();
        paid.MarkPaid(new TransactionFactory(_book).Expense(new DateOnly(2026, 4, 5), _bank.Id, _insurance.Id, -1000m, null, April));
        var manual = PlannedExpense.Create(_book, April, _insurance.Id, null, -1m, null);
        item.Update(_book, _insurance.Id, null, -9999m, null, RecurrenceFrequency.Monthly, [], BudgetMonth.FromKey(202601), null);

        var result = RecurringPlanner.Refresh(_book, April, [item], [paid, manual]);

        result.Updated.Should().BeEmpty();
        result.Skipped.Should().BeEmpty();
        paid.EstimatedAmount.Should().Be(-1000m);
    }

    [Fact]
    public void Refresh_reports_items_whose_source_is_no_longer_due()
    {
        var item = Item();
        var planned = Generated(item).Single();
        item.Update(_book, _insurance.Id, null, -1000m, "保險", RecurrenceFrequency.Monthly, [], BudgetMonth.FromKey(202601), BudgetMonth.FromKey(202603));

        var result = RecurringPlanner.Refresh(_book, April, [item], [planned]);

        result.Skipped.Should().Equal(new RecurringSkip(item.Id, planned.Id, RecurringSkipReason.NotDue));
    }

    [Fact]
    public void Generated_planned_expense_cannot_move_to_another_month()
    {
        var planned = Generated(Item()).Single();

        var act = () => planned.Update(_book, BudgetMonth.FromKey(202605), _insurance.Id, null, -1000m, null);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainException.RuleCode);
    }

    [Fact]
    public void Generated_planned_expense_can_still_be_edited_within_its_month()
    {
        var planned = Generated(Item()).Single();

        planned.Update(_book, April, _insurance.Id, null, -800m, "改金額");

        planned.EstimatedAmount.Should().Be(-800m);
    }

    private List<PlannedExpense> Generated(params RecurringPlannedExpense[] items) =>
        [.. RecurringPlanner.Generate(_book, April, items, new HashSet<RecurringPlannedExpenseId>()).Created];
}
