using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Transactions;

namespace SixJars.Domain.Ledger;

/// <summary>
/// 月可用餘額：純流量，依歸屬月份計算（spec §4.1）。
/// 收入 + 支出（電子錢包消費除外）− 加值 − 入新資金 − 貸款繳款總額 + 未付預定支出的預估金額。
/// </summary>
public sealed class DisposableBalanceCalculator(LedgerSnapshot ledger)
{
    public decimal Monthly(BudgetMonth month) =>
        ledger.Transactions.Where(t => t.BudgetMonth == month).Sum(Impact)
        + ledger.PlannedExpenses.Where(p => p.BudgetMonth == month && !p.IsPaid).Sum(p => p.EstimatedAmount);

    /// <summary>年累計餘額：同一年 1 月到指定月份的月可用餘額加總。</summary>
    public decimal YearToDate(BudgetMonth month) =>
        Enumerable.Range(1, month.Month).Sum(m => Monthly(new BudgetMonth(month.Year, m)));

    private decimal Impact(Transaction transaction) => transaction.Kind switch
    {
        TransactionKind.Income => transaction.Amount,
        // 電子錢包的消費已在加值時扣過，不可再扣一次（Q5）。
        TransactionKind.Expense when ledger.Book.GetAccount(transaction.AccountId).Type == AccountType.EWallet => 0m,
        TransactionKind.Expense => transaction.Amount,
        TransactionKind.TopUp or TransactionKind.FundAllocation or TransactionKind.LoanPayment => -transaction.Amount,
        _ => 0m,
    };
}
