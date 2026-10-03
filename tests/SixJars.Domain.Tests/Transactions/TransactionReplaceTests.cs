using FluentAssertions;
using SixJars.Domain.Common;
using SixJars.Domain.Transactions;
using Xunit;

namespace SixJars.Domain.Tests.Transactions;

public class TransactionReplaceTests
{
    [Fact]
    public void Replace_keeps_id_and_regenerates_postings()
    {
        var s = new SampleBook();
        var original = s.Factory.Expense(new DateOnly(2026, 1, 5), s.Cash.Id, s.Food.Id, -100m);
        var originalId = original.Id;
        var draft = s.Factory.Transfer(new DateOnly(2026, 1, 6), s.Bank.Id, s.Cash.Id, 500m);

        original.ReplaceWith(draft);

        original.Id.Should().Be(originalId);
        original.Id.Should().NotBe(draft.Id);
        original.Kind.Should().Be(TransactionKind.Transfer);
        original.Date.Should().Be(new DateOnly(2026, 1, 6));
        original.CategoryId.Should().BeNull();
        original.Postings.Should().Equal(new Posting(s.Bank.Id, -500m), new Posting(s.Cash.Id, 500m));
    }

    [Fact]
    public void Replace_with_draft_from_another_book_throws()
    {
        var mine = new SampleBook();
        var other = new SampleBook();
        var original = mine.Factory.Expense(new DateOnly(2026, 1, 5), mine.Cash.Id, mine.Food.Id, -100m);

        var act = () => original.ReplaceWith(other.Factory.Expense(new DateOnly(2026, 1, 5), other.Cash.Id, other.Food.Id, -1m));

        act.Should().Throw<DomainException>();
    }
}
