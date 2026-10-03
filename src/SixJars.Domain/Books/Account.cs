using SixJars.Domain.Common;

namespace SixJars.Domain.Books;

/// <summary>實際持有金錢或負債的地方。餘額採資產觀點：負債帳戶為負數。</summary>
public sealed class Account
{
    internal Account(AccountId id, string name, AccountType type, decimal openingBalance, bool countsAsAvailableCash)
    {
        Id = id;
        Name = name;
        Type = type;
        OpeningBalance = openingBalance;
        CountsAsAvailableCash = countsAsAvailableCash;
    }

    public AccountId Id { get; private set; }
    public string Name { get; private set; }
    public AccountType Type { get; private set; }
    public decimal OpeningBalance { get; private set; }
    /// <summary>只有現金帳戶可能為 true（例如外幣現鈔為 false）。</summary>
    public bool CountsAsAvailableCash { get; private set; }
    public bool IsLiability => Type is AccountType.CreditCard or AccountType.Loan;
}
