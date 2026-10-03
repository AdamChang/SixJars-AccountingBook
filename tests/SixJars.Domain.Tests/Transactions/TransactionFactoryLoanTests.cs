using FluentAssertions;
using SixJars.Domain.Common;
using SixJars.Domain.Transactions;
using Xunit;

namespace SixJars.Domain.Tests.Transactions;

public class TransactionFactoryLoanTests
{
    private static readonly DateOnly Jan9 = new(2026, 1, 9);
    private readonly SampleBook _s = new();

    [Fact]
    public void LoanDisbursement_increases_loan_and_receiver() =>
        _s.Factory.LoanDisbursement(Jan9, _s.Bank.Id, _s.Loan.Id, 500000m).Postings
            .Should().Equal(new Posting(_s.Loan.Id, -500000m), new Posting(_s.Bank.Id, 500000m));

    [Fact]
    public void LoanPayment_splits_principal_and_interest()
    {
        var tx = _s.Factory.LoanPayment(Jan9, _s.OtherBank.Id, _s.Loan.Id, 32503m, 28085m, _s.Mortgage.Id);

        tx.Postings.Should().Equal(new Posting(_s.OtherBank.Id, -32503m), new Posting(_s.Loan.Id, 28085m));
        tx.LoanPrincipal.Should().Be(28085m);
        tx.LoanInterest.Should().Be(4418m);
        tx.CategoryId.Should().Be(_s.Mortgage.Id);
    }

    [Fact]
    public void Prepayment_has_zero_interest() =>
        _s.Factory.LoanPayment(Jan9, _s.OtherBank.Id, _s.Loan.Id, 60000m, 60000m).LoanInterest.Should().Be(0m);

    [Fact]
    public void LoanPayment_rejects_principal_above_total()
    {
        var act = () => _s.Factory.LoanPayment(Jan9, _s.OtherBank.Id, _s.Loan.Id, 100m, 101m);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void LoanPayment_rejects_card_payer()
    {
        var act = () => _s.Factory.LoanPayment(Jan9, _s.Card.Id, _s.Loan.Id, 100m, 50m);
        act.Should().Throw<DomainException>();
    }
}
