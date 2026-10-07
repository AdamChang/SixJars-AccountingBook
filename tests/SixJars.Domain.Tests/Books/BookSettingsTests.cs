using FluentAssertions;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using Xunit;

namespace SixJars.Domain.Tests.Books;

public class BookSettingsTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 6, 1, 0, 0, TimeSpan.Zero);
    private readonly Book _book = new("測試帳本", new DateOnly(2025, 12, 30));

    [Fact]
    public void Added_items_are_appended_to_their_own_group()
    {
        var cash = _book.AddAccount("現金", AccountType.Cash);
        var bank = _book.AddAccount("銀行", AccountType.Bank);
        var fund = _book.AddPlanningFund("旅遊基金");
        var salary = _book.AddIncomeCategory("薪資");
        var food = _book.AddExpenseCategory("飲食", ExpenseNature.Floating);
        var transport = _book.AddExpenseCategory("交通", ExpenseNature.Floating);
        var lunch = _book.AddSubCategory(food.Id, "午餐");
        var dinner = _book.AddSubCategory(food.Id, "晚餐");
        var bus = _book.AddSubCategory(transport.Id, "公車");

        (cash.SortOrder, bank.SortOrder).Should().Be((0, 1));
        fund.SortOrder.Should().Be(0);
        // 收入主分類與支出主分類各自一組，都從 0 開始
        salary.SortOrder.Should().Be(0);
        (food.SortOrder, transport.SortOrder).Should().Be((0, 1));
        // 子分類以主分類為一組
        (lunch.SortOrder, dinner.SortOrder, bus.SortOrder).Should().Be((0, 1, 0));
    }

    [Fact]
    public void New_items_are_not_archived()
    {
        _book.AddAccount("現金", AccountType.Cash).ArchivedAt.Should().BeNull();
        _book.AddPlanningFund("旅遊基金").ArchivedAt.Should().BeNull();
        _book.AddIncomeCategory("薪資").IsArchived.Should().BeFalse();
    }

    [Fact]
    public void Rename_account_keeps_names_unique()
    {
        var cash = _book.AddAccount("現金", AccountType.Cash);
        _book.AddAccount("銀行", AccountType.Bank);

        _book.RenameAccount(cash.Id, "  零用金 ");
        cash.Name.Should().Be("零用金");
        _book.RenameAccount(cash.Id, "零用金");   // 改成原本的名字不算重複

        var act = () => _book.RenameAccount(cash.Id, "銀行");
        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainException.RuleCode);
    }

    [Fact]
    public void Rename_planning_fund_keeps_names_unique()
    {
        var travel = _book.AddPlanningFund("旅遊基金");
        _book.AddPlanningFund("緊急預備金");

        _book.RenamePlanningFund(travel.Id, "旅行基金");
        travel.Name.Should().Be("旅行基金");
        var act = () => _book.RenamePlanningFund(travel.Id, "緊急預備金");
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Main_category_names_are_unique_across_kinds()
    {
        _book.AddIncomeCategory("其它");
        var food = _book.AddExpenseCategory("飲食", ExpenseNature.Floating);

        var act = () => _book.RenameCategory(food.Id, "其它");
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Sub_category_names_are_unique_within_parent_only()
    {
        var food = _book.AddExpenseCategory("飲食", ExpenseNature.Floating);
        var transport = _book.AddExpenseCategory("交通", ExpenseNature.Floating);
        var lunch = _book.AddSubCategory(food.Id, "午餐");
        _book.AddSubCategory(food.Id, "晚餐");
        var bus = _book.AddSubCategory(transport.Id, "公車");

        _book.RenameCategory(bus.Id, "晚餐");   // 不同主分類可以同名
        bus.Name.Should().Be("晚餐");
        var act = () => _book.RenameCategory(lunch.Id, "晚餐");
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Only_cash_accounts_can_count_as_available_cash_when_changed()
    {
        var foreign = _book.AddAccount("外幣現鈔", AccountType.Cash, countsAsAvailableCash: false);
        var bank = _book.AddAccount("銀行", AccountType.Bank);

        _book.SetCountsAsAvailableCash(foreign.Id, true);
        foreign.CountsAsAvailableCash.Should().BeTrue();
        _book.SetCountsAsAvailableCash(bank.Id, false);   // 非現金帳戶設成 false 是允許的（本來就是 false）

        var act = () => _book.SetCountsAsAvailableCash(bank.Id, true);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Account_with_balance_cannot_be_archived()
    {
        var bank = _book.AddAccount("銀行", AccountType.Bank);

        var act = () => _book.ArchiveAccount(bank.Id, balance: 12.5m, At);
        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainException.NonZeroBalanceCode);
        bank.ArchivedAt.Should().BeNull();
    }

    [Fact]
    public void Archive_and_unarchive_account()
    {
        var bank = _book.AddAccount("銀行", AccountType.Bank);

        _book.ArchiveAccount(bank.Id, balance: 0m, At);
        _book.ArchiveAccount(bank.Id, balance: 0m, At.AddDays(1));   // 重複封存保留第一次的時間
        bank.ArchivedAt.Should().Be(At);

        _book.UnarchiveAccount(bank.Id);
        bank.ArchivedAt.Should().BeNull();
    }

    [Fact]
    public void Planning_fund_with_balance_cannot_be_archived()
    {
        var fund = _book.AddPlanningFund("旅遊基金");

        var act = () => _book.ArchivePlanningFund(fund.Id, balance: -1m, At);
        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainException.NonZeroBalanceCode);

        _book.ArchivePlanningFund(fund.Id, balance: 0m, At);
        fund.ArchivedAt.Should().Be(At);
        _book.UnarchivePlanningFund(fund.Id);
        fund.ArchivedAt.Should().BeNull();
    }

    [Fact]
    public void Archiving_main_category_leaves_sub_categories_untouched()
    {
        var food = _book.AddExpenseCategory("飲食", ExpenseNature.Floating);
        var lunch = _book.AddSubCategory(food.Id, "午餐");

        _book.ArchiveCategory(food.Id, At);

        food.ArchivedAt.Should().Be(At);
        lunch.ArchivedAt.Should().BeNull();
        _book.UnarchiveCategory(food.Id);
        food.IsArchived.Should().BeFalse();
    }

    [Fact]
    public void Reorder_accounts_renumbers_from_zero()
    {
        var a = _book.AddAccount("A", AccountType.Cash);
        var b = _book.AddAccount("B", AccountType.Bank);
        var c = _book.AddAccount("C", AccountType.Bank);

        _book.ReorderAccounts([c.Id, a.Id, b.Id]);

        (c.SortOrder, a.SortOrder, b.SortOrder).Should().Be((0, 1, 2));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("foreign")]
    public void Reorder_rejects_ids_that_do_not_match_the_group(string mismatch)
    {
        var a = _book.AddAccount("A", AccountType.Cash);
        var b = _book.AddAccount("B", AccountType.Bank);
        AccountId[] ids = mismatch switch
        {
            "missing" => [a.Id],
            // 筆數與組相同，必須靠集合比對才擋得下
            "duplicate" => [a.Id, a.Id],
            _ => [a.Id, AccountId.New()],
        };

        var act = () => _book.ReorderAccounts(ids);

        act.Should().Throw<DomainException>();
        (a.SortOrder, b.SortOrder).Should().Be((0, 1));
    }

    [Fact]
    public void Reorder_main_categories_only_touches_that_kind()
    {
        var salary = _book.AddIncomeCategory("薪資");
        var food = _book.AddExpenseCategory("飲食", ExpenseNature.Floating);
        var transport = _book.AddExpenseCategory("交通", ExpenseNature.Floating);

        _book.ReorderCategories(CategoryKind.Expense, parentId: null, [transport.Id, food.Id]);

        (transport.SortOrder, food.SortOrder, salary.SortOrder).Should().Be((0, 1, 0));
        var mixed = () => _book.ReorderCategories(CategoryKind.Expense, parentId: null, [transport.Id, food.Id, salary.Id]);
        mixed.Should().Throw<DomainException>();
    }

    [Fact]
    public void Reorder_sub_categories_of_one_parent()
    {
        var food = _book.AddExpenseCategory("飲食", ExpenseNature.Floating);
        var lunch = _book.AddSubCategory(food.Id, "午餐");
        var dinner = _book.AddSubCategory(food.Id, "晚餐");

        // 有 parentId 時以主分類為組，kind 參數不參與判斷
        _book.ReorderCategories(CategoryKind.Expense, food.Id, [dinner.Id, lunch.Id]);

        (dinner.SortOrder, lunch.SortOrder).Should().Be((0, 1));
    }

    [Fact]
    public void Reorder_planning_funds()
    {
        var a = _book.AddPlanningFund("A");
        var b = _book.AddPlanningFund("B");

        _book.ReorderPlanningFunds([b.Id, a.Id]);

        (b.SortOrder, a.SortOrder).Should().Be((0, 1));
    }

    [Fact]
    public void Referenced_item_cannot_be_removed()
    {
        var bank = _book.AddAccount("銀行", AccountType.Bank);

        var act = () => _book.RemoveAccount(bank.Id, isReferenced: true);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainException.InUseCode);
        _book.Accounts.Should().Contain(bank);
    }

    [Fact]
    public void Removing_compacts_sort_order_of_the_group()
    {
        var a = _book.AddAccount("A", AccountType.Cash);
        var b = _book.AddAccount("B", AccountType.Bank);
        var c = _book.AddAccount("C", AccountType.Bank);

        _book.RemoveAccount(b.Id, isReferenced: false);

        _book.Accounts.Should().BeEquivalentTo([a, c]);
        (a.SortOrder, c.SortOrder).Should().Be((0, 1));
        _book.AddAccount("D", AccountType.Bank).SortOrder.Should().Be(2);
    }

    [Fact]
    public void Remove_planning_fund_and_sub_category()
    {
        var fund = _book.AddPlanningFund("旅遊基金");
        var food = _book.AddExpenseCategory("飲食", ExpenseNature.Floating);
        var lunch = _book.AddSubCategory(food.Id, "午餐");
        var dinner = _book.AddSubCategory(food.Id, "晚餐");

        _book.RemovePlanningFund(fund.Id, isReferenced: false);
        _book.RemoveCategory(lunch.Id, isReferenced: false);

        _book.PlanningFunds.Should().BeEmpty();
        _book.Categories.Should().BeEquivalentTo([food, dinner]);
        dinner.SortOrder.Should().Be(0);
    }

    [Fact]
    public void Main_category_with_sub_categories_cannot_be_removed()
    {
        var food = _book.AddExpenseCategory("飲食", ExpenseNature.Floating);
        _book.AddSubCategory(food.Id, "午餐");

        var act = () => _book.RemoveCategory(food.Id, isReferenced: false);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainException.InUseCode);
    }

    [Fact]
    public void Changing_nature_applies_to_sub_categories()
    {
        var other = _book.AddExpenseCategory("其他", ExpenseNature.Special);
        var gift = _book.AddSubCategory(other.Id, "禮金");

        _book.ChangeExpenseNature(other.Id, ExpenseNature.Floating, hasPlannedExpenses: false);

        other.Nature.Should().Be(ExpenseNature.Floating);
        gift.Nature.Should().Be(ExpenseNature.Floating);
    }

    [Fact]
    public void Nature_can_only_change_on_expense_main_category()
    {
        var salary = _book.AddIncomeCategory("薪資");
        var food = _book.AddExpenseCategory("飲食", ExpenseNature.Floating);
        var lunch = _book.AddSubCategory(food.Id, "午餐");

        var onIncome = () => _book.ChangeExpenseNature(salary.Id, ExpenseNature.Fixed, hasPlannedExpenses: false);
        var onSub = () => _book.ChangeExpenseNature(lunch.Id, ExpenseNature.Fixed, hasPlannedExpenses: false);

        onIncome.Should().Throw<DomainException>();
        onSub.Should().Throw<DomainException>();
    }

    [Fact]
    public void Category_with_planned_expenses_cannot_become_floating()
    {
        var insurance = _book.AddExpenseCategory("保險", ExpenseNature.Fixed);

        var toFloating = () => _book.ChangeExpenseNature(insurance.Id, ExpenseNature.Floating, hasPlannedExpenses: true);
        toFloating.Should().Throw<DomainException>().Which.Code.Should().Be(DomainException.InUseCode);

        // 固定 → 特別仍然是預定支出允許的性質
        _book.ChangeExpenseNature(insurance.Id, ExpenseNature.Special, hasPlannedExpenses: true);
        insurance.Nature.Should().Be(ExpenseNature.Special);
    }

    [Theory]
    [InlineData(ExpenseNature.Special)]
    [InlineData(ExpenseNature.Floating)]
    public void Category_with_recurring_items_can_only_be_fixed_or_loan(ExpenseNature nature)
    {
        var rent = _book.AddExpenseCategory("房租", ExpenseNature.Fixed);

        var act = () => _book.ChangeExpenseNature(rent.Id, nature, hasPlannedExpenses: false, hasRecurringPlannedExpenses: true);

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainException.InUseCode);
        rent.Nature.Should().Be(ExpenseNature.Fixed);
    }

    [Fact]
    public void Category_with_recurring_items_can_switch_between_fixed_and_loan()
    {
        var rent = _book.AddExpenseCategory("房租", ExpenseNature.Fixed);

        _book.ChangeExpenseNature(rent.Id, ExpenseNature.Loan, hasPlannedExpenses: true, hasRecurringPlannedExpenses: true);

        rent.Nature.Should().Be(ExpenseNature.Loan);
    }
}
