using SixJars.Domain.Books;
using SixJars.Domain.Common;

namespace SixJars.Application.Ledger;

/// <summary>
/// 把 <see cref="ILedgerSummaryQuery"/> 的加總換算成餘額：期初加上變動。
/// 可用現金的篩選與 P1 的 <c>AvailableCashCalculator</c> 相同。
/// </summary>
public static class LedgerBalances
{
    public static decimal Account(Account account, IReadOnlyDictionary<AccountId, decimal> postingTotals) =>
        account.OpeningBalance + postingTotals.GetValueOrDefault(account.Id);

    public static decimal Fund(PlanningFund fund, IReadOnlyDictionary<PlanningFundId, decimal> fundDeltaTotals) =>
        fund.OpeningBalance + fundDeltaTotals.GetValueOrDefault(fund.Id);

    /// <summary>計入可用現金的現金帳戶餘額加總，可選擇加計電子錢包。</summary>
    public static decimal AvailableCash(Book book, IReadOnlyDictionary<AccountId, decimal> postingTotals, bool includeEWallets) =>
        book.Accounts
            .Where(a => a.CountsAsAvailableCash || (includeEWallets && a.Type == AccountType.EWallet))
            .Sum(a => Account(a, postingTotals));
}
