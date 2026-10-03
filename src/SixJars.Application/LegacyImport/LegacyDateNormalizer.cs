using SixJars.Domain.Common;

namespace SixJars.Application.LegacyImport;

/// <summary>修正舊記帳本中年份打錯的日期（例如 1 月表中的 2026-12-31 → 2025-12-31，spec §5.2）。</summary>
public static class LegacyDateNormalizer
{
    private const int ToleranceMonths = 6;

    public static (DateOnly Date, bool Corrected) Normalize(DateOnly date, BudgetMonth sheetMonth)
    {
        if (Math.Abs(MonthDistance(date.Year, date.Month, sheetMonth)) <= ToleranceMonths)
        {
            return (date, false);
        }

        var year = new[] { sheetMonth.Year - 1, sheetMonth.Year, sheetMonth.Year + 1 }
            .MinBy(candidate => Math.Abs(MonthDistance(candidate, date.Month, sheetMonth)));
        var day = Math.Min(date.Day, DateTime.DaysInMonth(year, date.Month));
        return (new DateOnly(year, date.Month, day), true);
    }

    private static int MonthDistance(int year, int month, BudgetMonth sheetMonth) =>
        (year * 12 + month) - (sheetMonth.Year * 12 + sheetMonth.Month);
}
