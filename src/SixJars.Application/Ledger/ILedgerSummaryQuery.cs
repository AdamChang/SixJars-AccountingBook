using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Reports;

namespace SixJars.Application.Ledger;

/// <summary>
/// 在資料庫端彙總分錄（spec §6）；回傳的是「分錄加總」，期初餘額由呼叫端從 <see cref="Book"/> 加上。
/// 規則必須與 P1 的計算器（<c>BalanceCalculator</c>、<c>DisposableBalanceCalculator</c>）一致，由 oracle 測試鎖住。
/// </summary>
public interface ILedgerSummaryQuery
{
    /// <summary>各帳戶的分錄加總；沒有分錄的帳戶不會出現在結果中。</summary>
    Task<IReadOnlyDictionary<AccountId, decimal>> PostingTotalsAsync(BookId bookId, BalanceCutoff cutoff, CancellationToken cancellationToken);

    /// <summary>各財務規劃帳戶的變動加總（入新資金與資金回流為正、出資金為負）；沒有變動的財務規劃帳戶不會出現在結果中。</summary>
    Task<IReadOnlyDictionary<PlanningFundId, decimal>> FundDeltaTotalsAsync(BookId bookId, BalanceCutoff cutoff, CancellationToken cancellationToken);

    /// <summary>每個歸屬月份的月可用餘額（含未付預定支出）；沒有資料的月份為 0。</summary>
    Task<IReadOnlyDictionary<BudgetMonth, decimal>> MonthlyDisposableAsync(Book book, BudgetMonth from, BudgetMonth to, CancellationToken cancellationToken);

    /// <summary>
    /// 某歸屬月份各分類的支出交易（<c>Expense</c>）金額加總，沿用交易的符號（支出為負、退款為正）；
    /// <b>包含</b>電子錢包帳戶的消費（ADR 0008，與月可用餘額不同）。沒有支出的分類不會出現在結果中。
    /// 預算的 actual（P4 L plan D3）與 M 段的月報共用。
    /// </summary>
    Task<IReadOnlyDictionary<CategoryId, decimal>> ExpenseTotalsByCategoryAsync(BookId bookId, BudgetMonth month, CancellationToken cancellationToken);

    /// <summary>
    /// 報表的資料來源（P4 M plan D2）：<paramref name="from"/>～<paramref name="to"/>（含）每個歸屬月份，
    /// 收入、支出、貸款繳款、入新資金、出資金、資金回流依（類型、分類、對方帳戶、財務規劃帳戶）加總，沿用交易的符號；
    /// <b>包含</b>電子錢包帳戶的消費（ADR 0008）。其他類型不會出現。
    /// </summary>
    Task<IReadOnlyList<ReportFlow>> ReportFlowsAsync(BookId bookId, BudgetMonth from, BudgetMonth to, CancellationToken cancellationToken);

    /// <summary>某歸屬月份未付預定支出依分類的預估金額加總（負數）；月報的「未付預定」小計（Q17'）。</summary>
    Task<IReadOnlyDictionary<CategoryId, decimal>> UnpaidPlannedByCategoryAsync(BookId bookId, BudgetMonth month, CancellationToken cancellationToken);
}
