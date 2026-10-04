using FluentAssertions;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using Xunit;

namespace SixJars.Domain.Tests.Planning;

public class PlannedExpenseUnpayTests
{
    private static readonly BudgetMonth March = new(2026, 3);
    private readonly SampleBook _s = new();

    [Fact]
    public void MarkUnpaid_clears_link()
    {
        var planned = PlannedExpense.Create(_s.Book, March, _s.Insurance.Id, _s.Card.Id, -1921m);
        planned.MarkPaid(_s.Factory.Expense(new DateOnly(2026, 3, 21), _s.Card.Id, _s.Insurance.Id, -1921m));

        planned.MarkUnpaid();

        planned.IsPaid.Should().BeFalse();
        planned.PaidTransactionId.Should().BeNull();
        // 回到未付之後可以再付一次，也可以修改。
        planned.MarkPaid(_s.Factory.Expense(new DateOnly(2026, 3, 22), _s.Card.Id, _s.Insurance.Id, -1900m));
        planned.IsPaid.Should().BeTrue();
    }

    [Fact]
    public void MarkUnpaid_on_unpaid_plan_throws()
    {
        var planned = PlannedExpense.Create(_s.Book, March, _s.Insurance.Id, _s.Card.Id, -1921m);

        var act = planned.MarkUnpaid;

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void MarkUnpaid_on_deleted_plan_throws()
    {
        var planned = PlannedExpense.Create(_s.Book, March, _s.Insurance.Id, _s.Card.Id, -1921m);
        var payment = _s.Factory.Expense(new DateOnly(2026, 3, 21), _s.Card.Id, _s.Insurance.Id, -1921m);
        planned.MarkPaid(payment);
        planned.Delete(new DateTimeOffset(2026, 3, 25, 0, 0, 0, TimeSpan.Zero));

        // 已刪除的資料不能修改（spec §3.3）；刪除付款交易時，query filter 本來就查不到已刪除的預定支出。
        var act = planned.MarkUnpaid;

        act.Should().Throw<DomainException>();
        planned.PaidTransactionId.Should().Be(payment.Id);
    }
}
