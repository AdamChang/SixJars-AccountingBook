using SixJars.Application.LegacyImport;
using SixJars.Application.Ledger;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Ledger;
using SixJars.Domain.Planning;
using SixJars.Domain.Reports;

namespace SixJars.AcceptanceTests;

/// <summary>
/// 把本系統算出的數字與 Excel 同月工作表逐項比對（取至小數 2 位）；已知的 Excel 差異見 <see cref="KnownExcelDifferences"/>。
/// P1 計算器（<see cref="Compare"/>）與 SQL 彙總（<see cref="CompareSqlAsync"/>）共用同一組指標與比對規則。
/// </summary>
internal static class MonthFigureComparison
{
    public static IReadOnlyList<string> Compare(LedgerSnapshot ledger, int year, LegacyMonthSheet sheet)
    {
        var month = new BudgetMonth(year, sheet.Month);
        var balances = new BalanceCalculator(ledger);
        return CompareFigures(ledger.Book, month, sheet, new SystemFigures(
            new DisposableBalanceCalculator(ledger).Monthly(month),
            new AvailableCashCalculator(ledger).ThroughBudgetMonth(month, includeEWallets: false),
            id => balances.AccountBalanceThroughBudgetMonth(id, month),
            id => balances.FundBalanceThroughBudgetMonth(id, month)));
    }

    /// <summary>以 <see cref="ILedgerSummaryQuery"/> 的歸屬月份截止結果加上期初，與 Excel 比對（spec §6 的真實資料驗收）。</summary>
    public static async Task<IReadOnlyList<string>> CompareSqlAsync(
        ILedgerSummaryQuery query, Book book, int year, LegacyMonthSheet sheet, CancellationToken cancellationToken)
    {
        var month = new BudgetMonth(year, sheet.Month);
        var cutoff = new BalanceCutoff.ThroughBudgetMonth(month);
        var postings = await query.PostingTotalsAsync(book.Id, cutoff, cancellationToken);
        var funds = await query.FundDeltaTotalsAsync(book.Id, cutoff, cancellationToken);
        var monthly = await query.MonthlyDisposableAsync(book, month, month, cancellationToken);
        return CompareFigures(book, month, sheet, new SystemFigures(
            monthly[month],
            LedgerBalances.AvailableCash(book, postings, includeEWallets: false),
            id => LedgerBalances.Account(book.GetAccount(id), postings),
            id => LedgerBalances.Fund(book.GetPlanningFund(id), funds)));
    }

    /// <summary>
    /// L 段驗收（spec §9）：各浮動主分類的預算 actual = 「預算」工作表的實際支出欄。Excel 為支出符號（負數），系統為正數。
    /// 對不上時逐筆列出，不調整規則；Excel 有、系統沒有的分類（匯入時略過的佔位名稱）只要求 Excel 為 0。
    /// </summary>
    public static async Task<IReadOnlyList<string>> CompareBudgetActualsAsync(
        ILedgerSummaryQuery query, Book book, int year, LegacyMonthSheet sheet, CancellationToken cancellationToken)
    {
        var month = new BudgetMonth(year, sheet.Month);
        var rows = BudgetSheet.Build(book, month, [], await query.ExpenseTotalsByCategoryAsync(book.Id, month, cancellationToken))
            .Rows.ToDictionary(r => r.CategoryId);
        var mismatches = new List<string>();
        var compared = new HashSet<CategoryId>();
        foreach (var excel in sheet.Figures.FloatingActuals)
        {
            var label = $"預算實際：{excel.Name}";
            var expected = -excel.Amount + KnownExcelDifferences.AdjustmentFor(sheet.Month, label);
            if (book.FindCategory(excel.Name) is not { } category || !rows.TryGetValue(category.Id, out var row))
            {
                if (Math.Round(expected, 2) != 0m)
                {
                    mismatches.Add($"{month} {label}：本系統沒有對應的浮動主分類，Excel {expected:N2}");
                }

                continue;
            }

            compared.Add(category.Id);
            if (Math.Round(row.Actual, 2) != Math.Round(expected, 2))
            {
                mismatches.Add($"{month} {label}：本系統 {row.Actual:N2}，Excel {expected:N2}，差 {row.Actual - expected:N2}");
            }
        }

        foreach (var row in rows.Values.Where(r => !compared.Contains(r.CategoryId) && r.Actual != 0m))
        {
            mismatches.Add($"{month} 預算實際：{book.GetCategory(row.CategoryId).Name} 不在 Excel 的預算表，本系統 {row.Actual:N2}");
        }

        return mismatches;
    }

    /// <summary>M 段驗收（spec §9）：月報 vs. 月工作表。口徑見 P4 M plan Task 7 的表。</summary>
    public static async Task<IReadOnlyList<string>> CompareMonthlyReportAsync(
        ILedgerSummaryQuery query, Book book, int year, LegacyMonthSheet sheet, CancellationToken cancellationToken)
    {
        var month = new BudgetMonth(year, sheet.Month);
        var report = MonthlyReport.Build(
            book, month,
            await query.ReportFlowsAsync(book.Id, month, month, cancellationToken),
            await query.UnpaidPlannedByCategoryAsync(book.Id, month, cancellationToken));
        var excel = sheet.Figures.Report ?? throw new InvalidOperationException($"{month} 沒有讀到月報對照數字");
        var mismatches = new List<string>();
        var checker = new Checker(sheet.Month, $"{month} ", mismatches);
        var floating = Section(report.Expenses, ReportSectionKind.Floating);

        checker.Named("月報收入", excel.IncomeItems, report.Income.Lines.ToDictionary(l => l.Name, l => l.Amount), sign: 1m);
        checker.Named("月報浮動", excel.FloatingMains, floating.Lines.ToDictionary(l => l.Name, l => l.Amount), sign: -1m);
        checker.Check("月報浮動合計", floating.Total, -excel.FloatingTotal);
        checker.Check("月報固定合計（含未付）", Paid(ReportSectionKind.Fixed), -excel.FixedTotal);
        checker.Check("月報貸款合計（含未付）", Paid(ReportSectionKind.Loan), -excel.LoanTotal);
        checker.Check("月報特別合計（含未付）", Paid(ReportSectionKind.Special), -excel.SpecialTotal);
        checker.Named("月報入新資金", excel.FundAllocations, report.Planning.ToDictionary(p => p.Name, p => p.Allocation), sign: 1m);
        checker.Named("月報出資金", excel.FundWithdrawals, report.Planning.ToDictionary(p => p.Name, p => p.Withdrawal), sign: -1m);
        checker.Named("月報資金回流", excel.FundReturns, report.Planning.ToDictionary(p => p.Name, p => p.Return), sign: 1m);
        return mismatches;

        decimal Paid(ReportSectionKind kind)
        {
            var section = Section(report.Expenses, kind);
            return section.Total + (section.Unpaid ?? 0m);
        }
    }

    /// <summary>M 段驗收（spec §9）：年報的年總計 vs.「總表」的 AU 欄；固定、貸款、特別加上全年的未付預定（年報本身不列）。</summary>
    public static async Task<IReadOnlyList<string>> CompareYearlyReportAsync(
        ILedgerSummaryQuery query, Book book, LegacyWorkbook workbook, CancellationToken cancellationToken)
    {
        var year = workbook.Settings.Year;
        var excel = workbook.YearTotals ?? throw new InvalidOperationException("沒有讀到「總表」");
        var report = YearlyReport.Build(
            book, year,
            await query.ReportFlowsAsync(book.Id, new BudgetMonth(year, 1), new BudgetMonth(year, 12), cancellationToken),
            new BudgetMonth(year + 1, 1));
        var unpaid = new Dictionary<ReportSectionKind, decimal>();
        for (var m = 1; m <= 12; m++)
        {
            var month = new BudgetMonth(year, m);
            var monthly = MonthlyReport.Build(book, month, [], await query.UnpaidPlannedByCategoryAsync(book.Id, month, cancellationToken));
            foreach (var section in monthly.Expenses.Where(s => s.Unpaid is not null))
            {
                unpaid[section.Kind] = unpaid.GetValueOrDefault(section.Kind) + section.Unpaid!.Value;
            }
        }

        var mismatches = new List<string>();
        var checker = new Checker(0, $"{year} 年 ", mismatches);   // 月份 0 = 年度（KnownExcelDifferences）
        var floating = YearSection(ReportSectionKind.Floating);
        checker.Named("年報收入", excel.IncomeItems, report.Income.Lines.ToDictionary(l => l.Name, l => l.Figures.Total), sign: 1m);
        checker.Named("年報浮動", excel.FloatingMains, floating.Lines.ToDictionary(l => l.Name, l => l.Figures.Total), sign: -1m);
        checker.Check("年報浮動合計", floating.Figures.Total, -excel.FloatingTotal);
        checker.Check("年報固定合計（含未付）", WithUnpaid(ReportSectionKind.Fixed), -excel.FixedTotal);
        checker.Check("年報貸款合計（含未付）", WithUnpaid(ReportSectionKind.Loan), -excel.LoanTotal);
        checker.Check("年報特別合計（含未付）", WithUnpaid(ReportSectionKind.Special), -excel.SpecialTotal);
        checker.Named("年報入新資金", excel.FundAllocations, report.Planning.Allocation.ToDictionary(l => l.Name, l => l.Figures.Total), sign: 1m);
        return mismatches;

        YearlyReportSection YearSection(ReportSectionKind kind) => report.Expenses.Single(s => s.Kind == kind);
        decimal WithUnpaid(ReportSectionKind kind) => YearSection(kind).Figures.Total + unpaid.GetValueOrDefault(kind);
    }

    private static MonthlyReportSection Section(IEnumerable<MonthlyReportSection> sections, ReportSectionKind kind) =>
        sections.Single(s => s.Kind == kind);

    /// <summary>比對一個數字並套用 <see cref="KnownExcelDifferences"/>；依名稱比對時，系統沒有的名稱只要求 Excel 為 0。</summary>
    private sealed class Checker(int month, string prefix, List<string> mismatches)
    {
        public void Check(string label, decimal actual, decimal excel)
        {
            var expected = excel + KnownExcelDifferences.AdjustmentFor(month, label);
            if (Math.Round(actual, 2) != Math.Round(expected, 2))
            {
                mismatches.Add($"{prefix}{label}：本系統 {actual:N2}，Excel {expected:N2}，差 {actual - expected:N2}");
            }
        }

        /// <param name="sign">Excel 原值換成系統口徑的符號：支出 −1、收入 1。</param>
        public void Named(string group, IEnumerable<LegacyNamedAmount> excel, IReadOnlyDictionary<string, decimal> system, decimal sign)
        {
            var compared = new HashSet<string>();
            foreach (var item in excel)
            {
                compared.Add(item.Name);
                Check($"{group}：{item.Name}", system.GetValueOrDefault(item.Name), sign * item.Amount);
            }

            foreach (var (name, amount) in system.Where(p => !compared.Contains(p.Key) && p.Value != 0m))
            {
                mismatches.Add($"{prefix}{group}：{name} 不在 Excel，本系統 {amount:N2}");
            }
        }
    }

    private static IReadOnlyList<string> CompareFigures(Book book, BudgetMonth month, LegacyMonthSheet sheet, SystemFigures system)
    {
        var figures = sheet.Figures;
        var mismatches = new List<string>();

        // Excel 的 J6 含「含電子錢包」加回的存量 N5，本系統是純流量（spec §4.3）
        Check("月可用餘額", system.MonthlyDisposable, figures.MonthlyDisposable - figures.WalletAddBack);
        Check("可用現金", system.AvailableCash, figures.AvailableCash);
        foreach (var f in figures.BankBalances.Concat(figures.EWalletBalances))
        {
            Check(f.Name, AccountBalance(f.Name), f.Amount);
        }

        foreach (var f in figures.CardOutstanding.Concat(figures.LoanRemaining))
        {
            Check(f.Name, -AccountBalance(f.Name), f.Amount);
        }

        foreach (var f in figures.FundBalances)
        {
            var fund = book.FindPlanningFund(f.Name);
            Check(f.Name, fund is null ? 0m : system.FundBalance(fund.Id), f.Amount);
        }

        return mismatches;

        decimal AccountBalance(string name) =>
            book.FindAccount(name) is { } account ? system.AccountBalance(account.Id) : 0m;

        void Check(string label, decimal actual, decimal excel)
        {
            var expected = excel + KnownExcelDifferences.AdjustmentFor(sheet.Month, label);
            if (Math.Round(actual, 2) != Math.Round(expected, 2))
            {
                mismatches.Add($"{month} {label}：本系統 {actual:N2}，Excel {expected:N2}，差 {actual - expected:N2}");
            }
        }
    }

    /// <summary>本系統對某個歸屬月份算出的數字；餘額一律是「截至該歸屬月份」。</summary>
    private sealed record SystemFigures(
        decimal MonthlyDisposable,
        decimal AvailableCash,
        Func<AccountId, decimal> AccountBalance,
        Func<PlanningFundId, decimal> FundBalance);
}
