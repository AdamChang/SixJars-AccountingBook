using SixJars.Domain.Common;
using SixJars.Domain.Transactions;

namespace SixJars.Domain.Ledger;

/// <summary>
/// 帳戶與財務規劃帳戶餘額。兩種截止方式並存（spec §4.4）：
/// 依日期（資產負債表）與依歸屬月份（驗收與月報表，對應 Excel 每張月表的截止方式）。
/// </summary>
public sealed class BalanceCalculator(LedgerSnapshot ledger)
{
    public decimal AccountBalanceAsOf(AccountId accountId, DateOnly date) =>
        ledger.Book.GetAccount(accountId).OpeningBalance + SumPostings(accountId, t => t.Date <= date);

    public decimal AccountBalanceThroughBudgetMonth(AccountId accountId, BudgetMonth month) =>
        ledger.Book.GetAccount(accountId).OpeningBalance + SumPostings(accountId, t => t.BudgetMonth <= month);

    public decimal FundBalanceAsOf(PlanningFundId fundId, DateOnly date) =>
        ledger.Book.GetPlanningFund(fundId).OpeningBalance + SumFund(fundId, t => t.Date <= date);

    public decimal FundBalanceThroughBudgetMonth(PlanningFundId fundId, BudgetMonth month) =>
        ledger.Book.GetPlanningFund(fundId).OpeningBalance + SumFund(fundId, t => t.BudgetMonth <= month);

    private decimal SumPostings(AccountId accountId, Func<Transaction, bool> include) =>
        ledger.Transactions.Where(include).SelectMany(t => t.Postings).Where(p => p.AccountId == accountId).Sum(p => p.Amount);

    private decimal SumFund(PlanningFundId fundId, Func<Transaction, bool> include) =>
        ledger.Transactions.Where(t => t.PlanningFundId == fundId).Where(include).Sum(t => t.FundDelta);
}
