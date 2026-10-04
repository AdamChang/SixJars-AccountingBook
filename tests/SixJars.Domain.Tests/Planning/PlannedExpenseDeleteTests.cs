using FluentAssertions;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using Xunit;

namespace SixJars.Domain.Tests.Planning;

public class PlannedExpenseDeleteTests
{
    private static readonly BudgetMonth March = new(2026, 3);
    private static readonly DateTimeOffset DeletedAt = new(2026, 3, 1, 3, 4, 5, TimeSpan.Zero);
    private readonly SampleBook _s = new();

    [Fact]
    public void PlannedExpense_delete_sets_deleted_at()
    {
        // 已付款的也可以刪除：刪除的是「計畫」本身，已建立的交易不受影響。
        var planned = PlannedExpense.Create(_s.Book, March, _s.Insurance.Id, _s.Card.Id, -1921m);
        var payment = _s.Factory.Expense(new DateOnly(2026, 3, 21), _s.Card.Id, _s.Insurance.Id, -1921m);
        planned.MarkPaid(payment);

        planned.Delete(DeletedAt);

        planned.IsDeleted.Should().BeTrue();
        planned.DeletedAt.Should().Be(DeletedAt);
        planned.PaidTransactionId.Should().Be(payment.Id);
        payment.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Update_deleted_plan_throws()
    {
        var planned = PlannedExpense.Create(_s.Book, March, _s.Insurance.Id, _s.Card.Id, -1921m);
        planned.Delete(DeletedAt);

        var act = () => planned.Update(_s.Book, March, _s.Insurance.Id, _s.Card.Id, -2000m, null);

        act.Should().Throw<DomainException>();
        planned.EstimatedAmount.Should().Be(-1921m);
    }

    [Fact]
    public void MarkPaid_deleted_plan_throws()
    {
        var planned = PlannedExpense.Create(_s.Book, March, _s.Insurance.Id, _s.Card.Id, -1921m);
        planned.Delete(DeletedAt);

        var act = () => planned.MarkPaid(_s.Factory.Expense(new DateOnly(2026, 3, 21), _s.Card.Id, _s.Insurance.Id, -1921m));

        act.Should().Throw<DomainException>();
        planned.IsPaid.Should().BeFalse();
    }
}
