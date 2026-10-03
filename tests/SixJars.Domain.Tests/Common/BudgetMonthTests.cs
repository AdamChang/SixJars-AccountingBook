using FluentAssertions;
using SixJars.Domain.Common;
using Xunit;

namespace SixJars.Domain.Tests.Common;

public class BudgetMonthTests
{
    [Fact]
    public void Of_date_takes_year_and_month() =>
        BudgetMonth.Of(new DateOnly(2025, 12, 31)).Should().Be(new BudgetMonth(2025, 12));

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Rejects_month_out_of_range(int month)
    {
        var act = () => new BudgetMonth(2026, month);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Compares_across_years()
    {
        (new BudgetMonth(2025, 12) < new BudgetMonth(2026, 1)).Should().BeTrue();
        (new BudgetMonth(2026, 3) >= new BudgetMonth(2026, 3)).Should().BeTrue();
    }

    [Fact]
    public void Key_round_trips_as_yyyymm()
    {
        var month = new BudgetMonth(2026, 2);
        month.Key.Should().Be(202602);
        BudgetMonth.FromKey(202602).Should().Be(month);
    }

    [Fact]
    public void Formats_as_iso_month() => new BudgetMonth(2026, 1).ToString().Should().Be("2026-01");
}
