using FluentAssertions;
using SixJars.Domain.Common;
using SixJars.Domain.Ledger;
using SixJars.Domain.Transactions;
using Xunit;

namespace SixJars.Domain.Tests.Transactions;

/// <summary>餘額調整（spec M3-1）：補匯舊年度時補在銜接點的差額。</summary>
public class TransactionFactoryAdjustmentTests
{
    private static readonly DateOnly Dec30 = new(2025, 12, 30);
    private static readonly BudgetMonth December = new(2025, 12);
    private readonly SampleBook _s = new();

    [Theory]
    [InlineData(-8506)]
    [InlineData(3017)]
    public void Account_adjustment_posts_the_signed_amount_to_any_account_type(int amount)
    {
        foreach (var account in new[] { _s.Bank, _s.Card, _s.Loan, _s.Wallet, _s.Cash })
        {
            var tx = new TransactionFactory(_s.Book).AccountAdjustment(Dec30, account.Id, amount, "補匯 2025 銜接調整", December);

            tx.Kind.Should().Be(TransactionKind.BalanceAdjustment);
            tx.Postings.Should().Equal(new Posting(account.Id, amount));
            tx.FundDelta.Should().Be(0m);
            tx.PlanningFundId.Should().BeNull();
            tx.BudgetMonth.Should().Be(December);
        }
    }

    [Fact]
    public void Fund_adjustment_changes_only_the_fund_and_hangs_on_cash()
    {
        var tx = _s.Factory.FundAdjustment(Dec30, _s.FreedomFund.Id, _s.Cash.Id, -5000m, null, December);

        tx.Kind.Should().Be(TransactionKind.BalanceAdjustment);
        tx.Postings.Should().BeEmpty();
        tx.FundDelta.Should().Be(-5000m);
        tx.AccountId.Should().Be(_s.Cash.Id);
        tx.PlanningFundId.Should().Be(_s.FreedomFund.Id);
    }

    [Fact]
    public void Fund_adjustment_requires_a_cash_account()
    {
        var act = () => _s.Factory.FundAdjustment(Dec30, _s.FreedomFund.Id, _s.Bank.Id, 100m);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Zero_adjustment_is_rejected()
    {
        var account = () => new TransactionFactory(_s.Book).AccountAdjustment(Dec30, _s.Bank.Id, 0m);
        var fund = () => new TransactionFactory(_s.Book).FundAdjustment(Dec30, _s.FreedomFund.Id, _s.Cash.Id, 0m);

        account.Should().Throw<DomainException>();
        fund.Should().Throw<DomainException>();
    }

    [Fact]
    public void Adjustments_move_balances_but_not_monthly_disposable()
    {
        var transactions = new[]
        {
            new TransactionFactory(_s.Book).AccountAdjustment(Dec30, _s.Bank.Id, -8506m, null, December),
            new TransactionFactory(_s.Book).FundAdjustment(Dec30, _s.FreedomFund.Id, _s.Cash.Id, 5000m, null, December),
        };
        var ledger = new LedgerSnapshot(_s.Book, transactions, []);
        var balances = new BalanceCalculator(ledger);

        balances.AccountBalanceAsOf(_s.Bank.Id, Dec30).Should().Be(_s.Bank.OpeningBalance - 8506m);
        balances.AccountBalanceAsOf(_s.Cash.Id, Dec30).Should().Be(_s.Cash.OpeningBalance);
        balances.FundBalanceAsOf(_s.FreedomFund.Id, Dec30).Should().Be(_s.FreedomFund.OpeningBalance + 5000m);
        new DisposableBalanceCalculator(ledger).Monthly(December).Should().Be(0m);
    }
}
