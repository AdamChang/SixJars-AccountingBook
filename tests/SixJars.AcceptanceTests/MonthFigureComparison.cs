using SixJars.Application.LegacyImport;
using SixJars.Domain.Common;
using SixJars.Domain.Ledger;

namespace SixJars.AcceptanceTests;

/// <summary>把本系統算出的數字與 Excel 同月工作表逐項比對（取至小數 2 位）；已知的 Excel 差異見 <see cref="KnownExcelDifferences"/>。</summary>
internal static class MonthFigureComparison
{
    public static IReadOnlyList<string> Compare(LedgerSnapshot ledger, int year, LegacyMonthSheet sheet)
    {
        var month = new BudgetMonth(year, sheet.Month);
        var figures = sheet.Figures;
        var balances = new BalanceCalculator(ledger);
        var mismatches = new List<string>();

        // Excel 的 J6 含「含電子錢包」加回的存量 N5，本系統是純流量（spec §4.3）
        Check("月可用餘額", new DisposableBalanceCalculator(ledger).Monthly(month), figures.MonthlyDisposable - figures.WalletAddBack);
        Check("可用現金", new AvailableCashCalculator(ledger).ThroughBudgetMonth(month, includeEWallets: false), figures.AvailableCash);
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
            var fund = ledger.Book.FindPlanningFund(f.Name);
            Check(f.Name, fund is null ? 0m : balances.FundBalanceThroughBudgetMonth(fund.Id, month), f.Amount);
        }

        return mismatches;

        decimal AccountBalance(string name) =>
            ledger.Book.FindAccount(name) is { } account ? balances.AccountBalanceThroughBudgetMonth(account.Id, month) : 0m;

        void Check(string label, decimal actual, decimal excel)
        {
            var expected = excel + KnownExcelDifferences.AdjustmentFor(sheet.Month, label);
            if (Math.Round(actual, 2) != Math.Round(expected, 2))
            {
                mismatches.Add($"{month} {label}：本系統 {actual:N2}，Excel {expected:N2}，差 {actual - expected:N2}");
            }
        }
    }
}
