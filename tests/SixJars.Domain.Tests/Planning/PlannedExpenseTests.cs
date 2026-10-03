using FluentAssertions;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using Xunit;

namespace SixJars.Domain.Tests.Planning;

public class PlannedExpenseTests
{
    private static readonly BudgetMonth March = new(2026, 3);
    private readonly SampleBook _s = new();

    [Fact]
    public void New_planned_expense_is_unpaid()
    {
        var planned = PlannedExpense.Create(_s.Book, March, _s.Phone.Id, _s.Bank.Id, -599m);

        planned.IsPaid.Should().BeFalse();
        planned.EstimatedAmount.Should().Be(-599m);
        planned.BookId.Should().Be(_s.Book.Id);
    }

    [Fact]
    public void Rejects_floating_category()
    {
        var act = () => PlannedExpense.Create(_s.Book, March, _s.Food.Id, null, -3000m);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void MarkPaid_links_transaction()
    {
        var planned = PlannedExpense.Create(_s.Book, March, _s.Insurance.Id, _s.Card.Id, -1921m);
        var tx = _s.Factory.Expense(new DateOnly(2026, 3, 21), _s.Card.Id, _s.Insurance.Id, -1921m);

        planned.MarkPaid(tx);

        planned.IsPaid.Should().BeTrue();
        planned.PaidTransactionId.Should().Be(tx.Id);
    }

    [Fact]
    public void MarkPaid_twice_throws()
    {
        var planned = PlannedExpense.Create(_s.Book, March, _s.Insurance.Id, _s.Card.Id, -1921m);
        var tx = _s.Factory.Expense(new DateOnly(2026, 3, 21), _s.Card.Id, _s.Insurance.Id, -1921m);
        planned.MarkPaid(tx);

        var act = () => planned.MarkPaid(tx);
        act.Should().Throw<DomainException>();
    }
}
