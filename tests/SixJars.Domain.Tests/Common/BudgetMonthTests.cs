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

    [Theory]
    [InlineData(190001)]
    [InlineData(202602)]
    [InlineData(299912)]
    public void IsValidKey_accepts_yyyymm_within_supported_years(int key) =>
        BudgetMonth.IsValidKey(key).Should().BeTrue();

    [Theory]
    [InlineData(1)]            // 第 0 年 1 月
    [InlineData(189912)]       // 年份下限前一個月
    [InlineData(300001)]       // 年份上限後一個月
    [InlineData(100000001)]    // 第 1000000 年 1 月
    [InlineData(202600)]       // 月份 0
    [InlineData(202613)]       // 月份 13
    [InlineData(-202601)]      // 負數
    public void IsValidKey_rejects_unsupported_year_or_month(int key) =>
        BudgetMonth.IsValidKey(key).Should().BeFalse();

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
