using SixJars.Domain.Books;
using SixJars.Domain.Common;

namespace SixJars.Domain.Ledger;

/// <summary>可用現金：計入可用現金的現金帳戶餘額加總，可選擇加計電子錢包。</summary>
public sealed class AvailableCashCalculator(LedgerSnapshot ledger)
{
    private readonly BalanceCalculator _balances = new(ledger);

    public decimal AsOf(DateOnly date, bool includeEWallets) =>
        Included(includeEWallets).Sum(a => _balances.AccountBalanceAsOf(a.Id, date));

    public decimal ThroughBudgetMonth(BudgetMonth month, bool includeEWallets) =>
        Included(includeEWallets).Sum(a => _balances.AccountBalanceThroughBudgetMonth(a.Id, month));

    private IEnumerable<Account> Included(bool includeEWallets) =>
        ledger.Book.Accounts.Where(a => a.CountsAsAvailableCash || (includeEWallets && a.Type == AccountType.EWallet));
}
