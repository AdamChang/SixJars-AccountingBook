using SixJars.Domain.Books;
using SixJars.Domain.Common;

namespace SixJars.Domain.Reports;

/// <param name="Months">1–12 月，正數口徑同月報。</param>
/// <param name="ShareOfIncome">年總計 ÷ 收入年總計（小數 4 位）；收入為 0 時為 null（D9）。</param>
/// <param name="MonthlyAverage">年總計 ÷ <see cref="YearlyReportResult.AverageDivisor"/>（小數 2 位）；未來的年份為 null。</param>
public sealed record YearlyFigures(IReadOnlyList<decimal> Months, decimal Total, decimal? ShareOfIncome, decimal? MonthlyAverage);

public sealed record YearlyReportLine(ReportLineKind Kind, Guid Id, string Name, YearlyFigures Figures, IReadOnlyList<YearlyReportLine> Children);

public sealed record YearlyReportSection(ReportSectionKind Kind, YearlyFigures Figures, IReadOnlyList<YearlyReportLine> Lines);

/// <summary>財務規劃依方向分三組（D10），每組依財務規劃帳戶列出。</summary>
public sealed record YearlyPlanning(
    IReadOnlyList<YearlyReportLine> Allocation, IReadOnlyList<YearlyReportLine> Withdrawal, IReadOnlyList<YearlyReportLine> Return);

/// <param name="AverageDivisor">
/// 月平均的分母（spec Q10）：從 <paramref name="AverageFromMonth"/> 算到結束月份（過去的年 12 月、今年 = 本月）的月份數；
/// 未來的年、整年都在開帳之前、或開帳月份還沒到時為 null。
/// </param>
/// <param name="AverageFromMonth">月平均從幾月算起：開帳那一年是開帳月份，其他年份是 1 月；沒有月平均時為 null。</param>
public sealed record YearlyReportResult(
    int Year,
    int? AverageDivisor,
    int? AverageFromMonth,
    YearlyReportSection Income,
    IReadOnlyList<YearlyReportSection> Expenses,
    YearlyFigures ExpenseTotal,
    YearlyPlanning Planning);

/// <summary>某個曆年（依歸屬月份的年）的年報表（spec §6.2）：只列實際交易，不列未付預定。</summary>
public static class YearlyReport
{
    /// <param name="today">台北時間的今天所在的月份。</param>
    public static YearlyReportResult Build(Book book, int year, IEnumerable<ReportFlow> flows, BudgetMonth today)
    {
        var tree = ReportTree.Build(book, flows, width: 12, m => m.Year == year ? m.Month - 1 : null);
        var (averageFrom, divisor) = AverageMonths(book.OpeningDate, year, today);
        var incomeTotal = tree.Income.Totals.Sum();

        YearlyFigures Figures(decimal[] months)
        {
            var total = months.Sum();
            return new YearlyFigures(
                months,
                total,
                incomeTotal == 0m ? null : Math.Round(total / incomeTotal, 4, MidpointRounding.AwayFromZero),
                divisor is { } d ? Math.Round(total / d, 2, MidpointRounding.AwayFromZero) : null);
        }

        YearlyReportLine Line(ReportNode node) =>
            new(node.Kind, node.Id, node.Name, Figures(node.Values), [.. node.Children.Select(Line)]);

        YearlyReportSection Section(ReportNodeSection section) =>
            new(section.Kind, Figures(section.Totals), [.. section.Lines.Select(Line)]);

        IReadOnlyList<YearlyReportLine> Funds(Func<ReportFundNode, decimal[]> pick) =>
        [
            .. tree.Funds
                .Where(f => pick(f).Any(v => v != 0m))
                .Select(f => new YearlyReportLine(ReportLineKind.PlanningFund, f.Fund.Id.Value, f.Fund.Name, Figures(pick(f)), [])),
        ];

        var expenseMonths = Enumerable.Range(0, 12).Select(i => tree.Expenses.Sum(s => s.Totals[i])).ToArray();
        return new YearlyReportResult(
            year,
            divisor,
            averageFrom,
            Section(tree.Income),
            [.. tree.Expenses.Select(Section)],
            Figures(expenseMonths),
            new YearlyPlanning(Funds(f => f.Allocation), Funds(f => f.Withdrawal), Funds(f => f.Return)));
    }

    /// <summary>開帳前的月份不可能有交易，不攤進月平均（spec Q10）。</summary>
    private static (int? From, int? Divisor) AverageMonths(DateOnly openingDate, int year, BudgetMonth today)
    {
        int? to = year < today.Year ? 12 : year == today.Year ? today.Month : null;
        if (to is not { } end || year < openingDate.Year)
        {
            return (null, null);
        }

        var from = year == openingDate.Year ? openingDate.Month : 1;
        return end >= from ? (from, end - from + 1) : (null, null);
    }
}
