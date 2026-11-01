using FluentAssertions;
using SixJars.Domain.Common;
using Xunit;

namespace SixJars.Domain.Tests.Transactions;

public class TransactionOriginTests
{
    private readonly SampleBook _s = new();

    [Fact]
    public void New_transaction_has_no_origin()
    {
        var transaction = _s.Factory.Expense(new DateOnly(2026, 1, 5), _s.Cash.Id, _s.Food.Id, -100m);

        transaction.CreatedVia.Should().BeNull();
        transaction.ExternalRef.Should().BeNull();
    }

    [Fact]
    public void Set_origin_records_via_and_external_ref()
    {
        var transaction = _s.Factory.Expense(new DateOnly(2026, 1, 5), _s.Cash.Id, _s.Food.Id, -100m);

        transaction.SetOrigin("mcp:claude.ai", "<abc@mail>");

        transaction.CreatedVia.Should().Be("mcp:claude.ai");
        transaction.ExternalRef.Should().Be("<abc@mail>");
    }

    [Fact]
    public void Set_origin_twice_throws()
    {
        var transaction = _s.Factory.Expense(new DateOnly(2026, 1, 5), _s.Cash.Id, _s.Food.Id, -100m);
        transaction.SetOrigin("mcp:claude.ai", null);

        var act = () => transaction.SetOrigin("mcp:other", "x");

        act.Should().Throw<DomainException>();
        transaction.CreatedVia.Should().Be("mcp:claude.ai");
    }

    [Fact]
    public void Set_empty_origin_is_no_op_and_allows_later_set()
    {
        var transaction = _s.Factory.Expense(new DateOnly(2026, 1, 5), _s.Cash.Id, _s.Food.Id, -100m);

        transaction.SetOrigin(null, null);
        transaction.SetOrigin("mcp:claude.ai", null);

        transaction.CreatedVia.Should().Be("mcp:claude.ai");
    }

    [Fact]
    public void Replace_keeps_origin()
    {
        var transaction = _s.Factory.Expense(new DateOnly(2026, 1, 5), _s.Cash.Id, _s.Food.Id, -100m);
        transaction.SetOrigin("mcp:claude.ai", "<abc@mail>");

        transaction.ReplaceWith(_s.Factory.Expense(new DateOnly(2026, 1, 6), _s.Cash.Id, _s.Food.Id, -200m));

        transaction.CreatedVia.Should().Be("mcp:claude.ai");
        transaction.ExternalRef.Should().Be("<abc@mail>");
    }
}
