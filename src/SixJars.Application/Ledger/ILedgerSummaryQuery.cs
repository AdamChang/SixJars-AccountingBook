using SixJars.Domain.Books;
using SixJars.Domain.Common;

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
}
