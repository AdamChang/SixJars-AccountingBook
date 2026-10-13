using SixJars.Domain.Common;
using SixJars.Domain.Transactions;

namespace SixJars.Domain.Reports;

/// <summary>
/// 報表的輸入：某歸屬月份、同一組（類型、分類、對方帳戶、財務規劃帳戶）的交易金額加總，沿用交易的符號（支出為負）。
/// 由 <c>ILedgerSummaryQuery.ReportFlowsAsync</c> 在資料庫端 GROUP BY 產生（P4 M plan D2）。
/// </summary>
/// <param name="LoanPrincipal">貸款繳款的本金加總；其他類型為 0。</param>
public sealed record ReportFlow(
    BudgetMonth Month,
    TransactionKind Kind,
    CategoryId? CategoryId,
    AccountId? CounterAccountId,
    PlanningFundId? PlanningFundId,
    decimal Amount,
    decimal LoanPrincipal = 0m);
