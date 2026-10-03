using FluentAssertions;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using Xunit;

namespace SixJars.Domain.Tests.Books;

public class BookTests
{
    private readonly Book _book = new("測試帳本", new DateOnly(2025, 12, 30));

    [Fact]
    public void AddAccount_keeps_type_and_opening_balance()
    {
        var card = _book.AddAccount("國泰Combo卡", AccountType.CreditCard, -22668m);

        card.Type.Should().Be(AccountType.CreditCard);
        card.OpeningBalance.Should().Be(-22668m);
        card.IsLiability.Should().BeTrue();
        _book.GetAccount(card.Id).Should().BeSameAs(card);
    }

    [Fact]
    public void Rejects_duplicate_account_name()
    {
        _book.AddAccount("現金", AccountType.Cash);
        var act = () => _book.AddAccount("現金", AccountType.Cash);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Only_cash_accounts_can_count_as_available_cash()
    {
        _book.AddAccount("外幣現鈔", AccountType.Cash, countsAsAvailableCash: false).CountsAsAvailableCash.Should().BeFalse();
        _book.AddAccount("現金", AccountType.Cash).CountsAsAvailableCash.Should().BeTrue();
        _book.AddAccount("國泰世華銀行", AccountType.Bank).CountsAsAvailableCash.Should().BeFalse();
    }

    [Fact]
    public void Sub_category_inherits_kind_and_nature()
    {
        var fixedMain = _book.AddExpenseCategory("固定支出", ExpenseNature.Fixed);
        var insurance = _book.AddSubCategory(fixedMain.Id, "保險費");

        insurance.Kind.Should().Be(CategoryKind.Expense);
        insurance.Nature.Should().Be(ExpenseNature.Fixed);
        insurance.ParentId.Should().Be(fixedMain.Id);
    }

    [Fact]
    public void Categories_have_only_two_levels()
    {
        var main = _book.AddExpenseCategory("主食", ExpenseNature.Floating);
        var sub = _book.AddSubCategory(main.Id, "早餐");
        var act = () => _book.AddSubCategory(sub.Id, "燒餅");
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void FindCategory_by_main_and_sub_name()
    {
        var main = _book.AddIncomeCategory("其它收入");
        var lottery = _book.AddSubCategory(main.Id, "中獎");

        _book.FindCategory("其它收入").Should().BeSameAs(main);
        _book.FindCategory("其它收入", "中獎").Should().BeSameAs(lottery);
        _book.FindCategory("其它收入", "不存在").Should().BeNull();
    }

    [Fact]
    public void GetAccount_with_unknown_id_throws()
    {
        var act = () => _book.GetAccount(AccountId.New());
        act.Should().Throw<DomainException>();
    }
}
