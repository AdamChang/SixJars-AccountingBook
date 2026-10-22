using SixJars.Domain.Books;
using SixJars.Domain.Common;

namespace SixJars.Domain.Reports;

/// <param name="Amount">正數口徑：收入 = 收入金額、支出 = −支出金額（退款抵銷）、貸款帳戶 = 繳款總額。</param>
/// <param name="Principal">只有貸款帳戶列有值。</param>
/// <param name="Interest">只有貸款帳戶列有值：總額 − 本金。</param>
public sealed record MonthlyReportLine(
    ReportLineKind Kind, Guid Id, string Name, decimal Amount, decimal? Principal, decimal? Interest, IReadOnlyList<MonthlyReportLine> Children);

/// <param name="Unpaid">未付預定支出的小計（不計入 <paramref name="Total"/>）；固定、貸款、特別三區有值（Q17'），其他為 null。</param>
public sealed record MonthlyReportSection(ReportSectionKind Kind, decimal Total, decimal? Unpaid, IReadOnlyList<MonthlyReportLine> Lines);

public sealed record MonthlyPlanningLine(Guid FundId, string Name, decimal Allocation, decimal Withdrawal, decimal Return);

/// <param name="Expenses">固定依浮動、固定、貸款、特別排列（D7）。</param>
/// <param name="ExpenseTotal">四個支出區塊的合計；不含未付預定與財務規劃（ADR 0008）。</param>
public sealed record MonthlyReportResult(
    BudgetMonth Month,
    MonthlyReportSection Income,
    IReadOnlyList<MonthlyReportSection> Expenses,
    decimal ExpenseTotal,
    decimal UnpaidTotal,
    IReadOnlyList<MonthlyPlanningLine> Planning);

/// <summary>某個歸屬月份的月報表（spec §6.1、§6.2）。</summary>
public static class MonthlyReport
{
    /// <param name="unpaidByCategory">該月未付預定支出依分類的預估金額加總（負數），即 <c>ILedgerSummaryQuery.UnpaidPlannedByCategoryAsync</c> 的結果。</param>
    public static MonthlyReportResult Build(
        Book book, BudgetMonth month, IEnumerable<ReportFlow> flows, IReadOnlyDictionary<CategoryId, decimal> unpaidByCategory)
    {
        var tree = ReportTree.Build(book, flows, width: 1, m => m == month ? 0 : null);
        var unpaid = unpaidByCategory
            .Select(p => (Section: ReportTree.SectionOf(book, p.Key), p.Value))
            .Where(p => p.Section is not null)
            .GroupBy(p => p.Section!.Value)
            .ToDictionary(g => g.Key, g => -g.Sum(p => p.Value));

        var expenses = tree.Expenses
            .Select(s => new MonthlyReportSection(
                s.Kind,
                s.Totals[0],
                s.Kind == ReportSectionKind.Floating ? null : unpaid.GetValueOrDefault(s.Kind),
                [.. s.Lines.Select(Line)]))
            .ToList();
        var planning = tree.Funds
            .Select(f => new MonthlyPlanningLine(f.Fund.Id.Value, f.Fund.Name, f.Allocation[0], f.Withdrawal[0], f.Return[0]))
            .ToList();

        return new MonthlyReportResult(
            month,
            new MonthlyReportSection(ReportSectionKind.Income, tree.Income.Totals[0], null, [.. tree.Income.Lines.Select(Line)]),
            expenses,
            expenses.Sum(s => s.Total),
            expenses.Sum(s => s.Unpaid ?? 0m),
            planning);
    }

    private static MonthlyReportLine Line(ReportNode node)
    {
        var amount = node.Values[0];
        var principal = node.Principal?[0];
        return new MonthlyReportLine(
            node.Kind, node.Id, node.Name, amount, principal, principal is { } p ? amount - p : null, [.. node.Children.Select(Line)]);
    }
}
