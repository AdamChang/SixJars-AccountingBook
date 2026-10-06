using FluentAssertions;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using Xunit;

namespace SixJars.Domain.Tests.Books;

public class BookSettingsTests
{
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
}
