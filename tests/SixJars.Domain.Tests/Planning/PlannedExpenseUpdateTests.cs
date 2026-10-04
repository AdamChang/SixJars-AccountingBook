using FluentAssertions;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using Xunit;

namespace SixJars.Domain.Tests.Planning;

public class PlannedExpenseUpdateTests
{
    private static readonly BudgetMonth March = new(2026, 3);
    private static readonly BudgetMonth April = new(2026, 4);
    private readonly SampleBook _s = new();

    [Fact]
    public void Update_changes_unpaid_plan()
    {
        var planned = PlannedExpense.Create(_s.Book, March, _s.Insurance.Id, _s.Card.Id, -1921m, "原本");
        var id = planned.Id;

        planned.Update(_s.Book, April, _s.Phone.Id, _s.Bank.Id, -599m, "改過");

        planned.Id.Should().Be(id);
        planned.BudgetMonth.Should().Be(April);
        planned.CategoryId.Should().Be(_s.Phone.Id);
        planned.AccountId.Should().Be(_s.Bank.Id);
        planned.EstimatedAmount.Should().Be(-599m);
        planned.Note.Should().Be("改過");
        planned.IsPaid.Should().BeFalse();
    }

    [Fact]
    public void Update_after_paid_throws()
    {
        var planned = PlannedExpense.Create(_s.Book, March, _s.Insurance.Id, _s.Card.Id, -1921m);
        planned.MarkPaid(_s.Factory.Expense(new DateOnly(2026, 3, 21), _s.Card.Id, _s.Insurance.Id, -1921m));

        var act = () => planned.Update(_s.Book, March, _s.Insurance.Id, _s.Card.Id, -2000m, null);

        act.Should().Throw<DomainException>();
        planned.EstimatedAmount.Should().Be(-1921m);
    }
}
