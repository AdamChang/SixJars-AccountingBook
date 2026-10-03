using FluentAssertions;
using SixJars.Domain.Common;
using SixJars.Domain.Transactions;
using Xunit;

namespace SixJars.Domain.Tests.Transactions;

public class TransactionFactoryFundTests
{
    private static readonly DateOnly Jan11 = new(2026, 1, 11);
    private readonly SampleBook _s = new();

    [Fact]
    public void Allocation_with_transfer_moves_money_and_fills_fund()
    {
        var tx = _s.Factory.FundAllocation(Jan11, _s.FreedomFund.Id, _s.Investment.Id, _s.Bank.Id, 8000m);

        tx.Postings.Should().Equal(new Posting(_s.Bank.Id, -8000m), new Posting(_s.Investment.Id, 8000m));
        tx.FundDelta.Should().Be(8000m);
        tx.PlanningFundId.Should().Be(_s.FreedomFund.Id);
    }

    [Fact]
    public void Allocation_within_same_account_has_no_postings()
    {
        var tx = _s.Factory.FundAllocation(Jan11, _s.FreedomFund.Id, _s.Bank.Id, null, 8000m);

        tx.Postings.Should().BeEmpty();
        tx.FundDelta.Should().Be(8000m);
    }

    [Fact]
    public void Allocation_with_from_equal_to_is_treated_as_same_account()
    {
        var tx = _s.Factory.FundAllocation(Jan11, _s.FreedomFund.Id, _s.Bank.Id, _s.Bank.Id, 8000m);

        tx.Postings.Should().BeEmpty();
        tx.CounterAccountId.Should().BeNull();
    }

    [Fact]
    public void Withdrawal_reduces_account_and_fund()
    {
        var tx = _s.Factory.FundWithdrawal(Jan11, _s.FreedomFund.Id, _s.Investment.Id, 5492m);

        tx.Postings.Should().Equal(new Posting(_s.Investment.Id, -5492m));
        tx.FundDelta.Should().Be(-5492m);
    }

    [Fact]
    public void Withdrawal_accepts_optional_category() =>
        _s.Factory.FundWithdrawal(Jan11, _s.FreedomFund.Id, _s.Investment.Id, 5492m, _s.Food.Id)
            .CategoryId.Should().Be(_s.Food.Id);

    [Fact]
    public void Return_increases_account_and_fund()
    {
        var tx = _s.Factory.FundReturn(Jan11, _s.FreedomFund.Id, _s.Investment.Id, 254m);

        tx.Postings.Should().Equal(new Posting(_s.Investment.Id, 254m));
        tx.FundDelta.Should().Be(254m);
    }

    [Fact]
    public void Allocation_rejects_unknown_fund()
    {
        var act = () => _s.Factory.FundAllocation(Jan11, PlanningFundId.New(), _s.Bank.Id, null, 1m);
        act.Should().Throw<DomainException>();
    }
}
