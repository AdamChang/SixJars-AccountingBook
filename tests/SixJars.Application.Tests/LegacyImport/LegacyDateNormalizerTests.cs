using FluentAssertions;
using SixJars.Application.LegacyImport;
using SixJars.Domain.Common;
using Xunit;

namespace SixJars.Application.Tests.LegacyImport;

public class LegacyDateNormalizerTests
{
    [Fact]
    public void Mistyped_year_is_corrected_to_nearest_sheet_month() =>
        LegacyDateNormalizer.Normalize(new DateOnly(2026, 12, 31), new BudgetMonth(2026, 1))
            .Should().Be((new DateOnly(2025, 12, 31), true));

    [Fact]
    public void Previous_month_end_salary_is_kept() =>
        LegacyDateNormalizer.Normalize(new DateOnly(2025, 12, 31), new BudgetMonth(2026, 1))
            .Should().Be((new DateOnly(2025, 12, 31), false));

    [Fact]
    public void Date_within_sheet_month_is_kept() =>
        LegacyDateNormalizer.Normalize(new DateOnly(2026, 1, 30), new BudgetMonth(2026, 2))
            .Should().Be((new DateOnly(2026, 1, 30), false));
}
