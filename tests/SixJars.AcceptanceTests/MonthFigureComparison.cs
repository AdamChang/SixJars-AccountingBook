using SixJars.Application.LegacyImport;
using SixJars.Application.Ledger;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Ledger;

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
