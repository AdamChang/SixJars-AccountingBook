using FluentAssertions;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Transactions;
using Xunit;

namespace SixJars.Domain.Tests.Transactions;

public class TransactionTagTests
{
    private static readonly DateOnly Date = new(2026, 1, 5);
    private readonly SampleBook _s = new();
    private readonly Tag _trip;
    private readonly Tag _work;

    public TransactionTagTests()
    {
        _trip = _s.Book.AddTag("旅遊", TagColor.Teal);
        _work = _s.Book.AddTag("工作", TagColor.Blue);
    }

    public static TheoryData<TransactionKind> AllKinds => [.. Enum.GetValues<TransactionKind>()];

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Every_kind_can_have_tags(TransactionKind kind)
    {
        var transaction = _s.Factory.WithTags(Create(kind), [_trip.Id, _work.Id]);

        transaction.Kind.Should().Be(kind);
        transaction.TagIds.Should().BeEquivalentTo([_trip.Id, _work.Id]);
    }

    [Fact]
    public void New_transaction_has_no_tags()
    {
        Create(TransactionKind.Expense).TagIds.Should().BeEmpty();
    }

    [Fact]
    public void Up_to_four_tags_are_allowed()
    {
        var ids = Enumerable.Range(0, Transaction.MaxTags).Select(i => _s.Book.AddTag($"T{i}", TagColor.Red).Id).ToList();

        _s.Factory.WithTags(Create(TransactionKind.Expense), ids).TagIds.Should().BeEquivalentTo(ids);
    }

    [Fact]
    public void More_than_four_tags_are_rejected()
    {
        var ids = Enumerable.Range(0, Transaction.MaxTags + 1).Select(i => _s.Book.AddTag($"T{i}", TagColor.Red).Id).ToList();

        var act = () => _s.Factory.WithTags(Create(TransactionKind.Expense), ids);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Duplicate_tags_are_rejected()
    {
        var act = () => _s.Factory.WithTags(Create(TransactionKind.Expense), [_trip.Id, _trip.Id]);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Tag_from_another_book_is_rejected()
    {
        var other = new SampleBook().Book.AddTag("旅遊", TagColor.Teal);

        var act = () => _s.Factory.WithTags(Create(TransactionKind.Expense), [other.Id]);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Factory_allows_archived_tags()
    {
        // 封存檢查在寫入 command（spec D2），還原備份要能重建掛著封存標籤的交易。
        _s.Book.ArchiveTag(_trip.Id, DateTimeOffset.UnixEpoch);

        _s.Factory.WithTags(Create(TransactionKind.Expense), [_trip.Id]).TagIds.Should().Equal(_trip.Id);
    }

    [Fact]
    public void Replace_takes_tags_from_draft()
    {
        var original = _s.Factory.WithTags(Create(TransactionKind.Expense), [_trip.Id]);

        original.ReplaceWith(_s.Factory.WithTags(Create(TransactionKind.Expense), [_work.Id, _trip.Id]));
        original.TagIds.Should().BeEquivalentTo([_trip.Id, _work.Id]);

        original.ReplaceWith(Create(TransactionKind.Income));
        original.TagIds.Should().BeEmpty();
    }

    private Transaction Create(TransactionKind kind) => kind switch
    {
        TransactionKind.Income => _s.Factory.Income(Date, _s.Bank.Id, _s.Salary.Id, 100m),
        TransactionKind.Expense => _s.Factory.Expense(Date, _s.Cash.Id, _s.Food.Id, -100m),
        TransactionKind.Transfer => _s.Factory.Transfer(Date, _s.Bank.Id, _s.OtherBank.Id, 100m),
        TransactionKind.Withdrawal => _s.Factory.Withdrawal(Date, _s.Bank.Id, _s.Cash.Id, 100m),
        TransactionKind.CashDeposit => _s.Factory.CashDeposit(Date, _s.Bank.Id, _s.Cash.Id, 100m),
        TransactionKind.TopUp => _s.Factory.TopUp(Date, _s.Wallet.Id, _s.Cash.Id, 100m),
        TransactionKind.CardPayment => _s.Factory.CardPayment(Date, _s.Bank.Id, _s.Card.Id, 100m),
        TransactionKind.LoanDisbursement => _s.Factory.LoanDisbursement(Date, _s.Bank.Id, _s.Loan.Id, 100m),
        TransactionKind.LoanPayment => _s.Factory.LoanPayment(Date, _s.Bank.Id, _s.Loan.Id, 100m, 80m),
        TransactionKind.FundAllocation => _s.Factory.FundAllocation(Date, _s.FreedomFund.Id, _s.Investment.Id, _s.Bank.Id, 100m),
        TransactionKind.FundWithdrawal => _s.Factory.FundWithdrawal(Date, _s.FreedomFund.Id, _s.Investment.Id, 1m),
        TransactionKind.FundReturn => _s.Factory.FundReturn(Date, _s.FreedomFund.Id, _s.Investment.Id, 100m),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
