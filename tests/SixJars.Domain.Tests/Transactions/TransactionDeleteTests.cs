using FluentAssertions;
using SixJars.Domain.Common;
using SixJars.Domain.Transactions;
using Xunit;

namespace SixJars.Domain.Tests.Transactions;

public class TransactionDeleteTests
{
    private static readonly DateTimeOffset DeletedAt = new(2026, 2, 1, 3, 4, 5, TimeSpan.Zero);
    private readonly SampleBook _s = new();

    [Fact]
    public void Delete_sets_deleted_at()
    {
        var transaction = _s.Factory.Expense(new DateOnly(2026, 1, 5), _s.Cash.Id, _s.Food.Id, -100m);
        transaction.IsDeleted.Should().BeFalse();

        transaction.Delete(DeletedAt);

        transaction.IsDeleted.Should().BeTrue();
        transaction.DeletedAt.Should().Be(DeletedAt);
    }

    [Fact]
    public void Delete_twice_throws()
    {
        var transaction = _s.Factory.Expense(new DateOnly(2026, 1, 5), _s.Cash.Id, _s.Food.Id, -100m);
        transaction.Delete(DeletedAt);

        var act = () => transaction.Delete(DeletedAt.AddDays(1));

        act.Should().Throw<DomainException>();
        transaction.DeletedAt.Should().Be(DeletedAt);
    }

    [Fact]
    public void Replace_deleted_transaction_throws()
    {
        var transaction = _s.Factory.Expense(new DateOnly(2026, 1, 5), _s.Cash.Id, _s.Food.Id, -100m);
        transaction.Delete(DeletedAt);

        // spec §3.3：已刪除的資料不能修改。
        var act = () => transaction.ReplaceWith(_s.Factory.Expense(new DateOnly(2026, 1, 6), _s.Cash.Id, _s.Food.Id, -200m));

        act.Should().Throw<DomainException>();
        transaction.Amount.Should().Be(-100m);
    }
}
