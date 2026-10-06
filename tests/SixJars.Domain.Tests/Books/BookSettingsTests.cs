using FluentAssertions;
using SixJars.Domain.Books;
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
}
