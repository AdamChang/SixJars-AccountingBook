using FluentAssertions;
using SixJars.Domain.Common;
using SixJars.Domain.Transactions;
using Xunit;

namespace SixJars.Domain.Tests.Transactions;

public class TransactionFactoryBasicTests
{
    private static readonly DateOnly Jan5 = new(2026, 1, 5);
    private readonly SampleBook _s = new();

    [Fact]
    public void Income_posts_amount_to_account()
    {
        var tx = _s.Factory.Income(Jan5, _s.Bank.Id, _s.Salary.Id, 84223m);

        tx.Kind.Should().Be(TransactionKind.Income);
        tx.BookId.Should().Be(_s.Book.Id);
        tx.BudgetMonth.Should().Be(new BudgetMonth(2026, 1));
        tx.Postings.Should().Equal(new Posting(_s.Bank.Id, 84223m));
    }

    [Fact]
    public void Budget_month_can_differ_from_date()
    {
        var tx = _s.Factory.Income(new DateOnly(2025, 12, 31), _s.Bank.Id, _s.Salary.Id, 84223m, budgetMonth: new BudgetMonth(2026, 1));

        tx.Date.Should().Be(new DateOnly(2025, 12, 31));
        tx.BudgetMonth.Should().Be(new BudgetMonth(2026, 1));
    }

    [Fact]
    public void Expense_on_card_increases_liability() =>
        _s.Factory.Expense(Jan5, _s.Card.Id, _s.Food.Id, -129m).Postings
            .Should().Equal(new Posting(_s.Card.Id, -129m));

    [Fact]
    public void Expense_rejects_income_category()
    {
        var act = () => _s.Factory.Expense(Jan5, _s.Bank.Id, _s.Salary.Id, -1m);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Transfer_moves_money_from_to()
    {
        var tx = _s.Factory.Transfer(Jan5, _s.Bank.Id, _s.OtherBank.Id, 34000m);

        tx.AccountId.Should().Be(_s.Bank.Id);
        tx.CounterAccountId.Should().Be(_s.OtherBank.Id);
        tx.Postings.Should().Equal(new Posting(_s.Bank.Id, -34000m), new Posting(_s.OtherBank.Id, 34000m));
    }

    [Fact]
    public void Transfer_rejects_non_positive_amount()
    {
        var act = () => _s.Factory.Transfer(Jan5, _s.Bank.Id, _s.OtherBank.Id, -1m);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Transfer_rejects_same_account()
    {
        var act = () => _s.Factory.Transfer(Jan5, _s.Bank.Id, _s.Bank.Id, 1m);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Withdrawal_moves_bank_to_cash() =>
        _s.Factory.Withdrawal(Jan5, _s.Bank.Id, _s.Cash.Id, 3000m).Postings
            .Should().Equal(new Posting(_s.Bank.Id, -3000m), new Posting(_s.Cash.Id, 3000m));

    [Fact]
    public void CashDeposit_moves_cash_to_bank() =>
        _s.Factory.CashDeposit(Jan5, _s.Bank.Id, _s.Cash.Id, 20000m).Postings
            .Should().Equal(new Posting(_s.Cash.Id, -20000m), new Posting(_s.Bank.Id, 20000m));

    [Fact]
    public void TopUp_from_card_increases_wallet_and_card_liability() =>
        _s.Factory.TopUp(Jan5, _s.Wallet.Id, _s.Card.Id, 3000m).Postings
            .Should().Equal(new Posting(_s.Card.Id, -3000m), new Posting(_s.Wallet.Id, 3000m));

    [Fact]
    public void CardPayment_moves_bank_to_card() =>
        _s.Factory.CardPayment(Jan5, _s.Bank.Id, _s.Card.Id, 18197m).Postings
            .Should().Equal(new Posting(_s.Bank.Id, -18197m), new Posting(_s.Card.Id, 18197m));

    [Fact]
    public void CardPayment_rejects_wallet_payer()
    {
        var act = () => _s.Factory.CardPayment(Jan5, _s.Wallet.Id, _s.Card.Id, 100m);
        act.Should().Throw<DomainException>();
    }
}
