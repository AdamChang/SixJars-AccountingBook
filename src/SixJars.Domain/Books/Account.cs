using SixJars.Domain.Common;

namespace SixJars.Domain.Books;

/// <summary>實際持有金錢或負債的地方。餘額採資產觀點：負債帳戶為負數。</summary>
public sealed class Account
{
    internal Account(AccountId id, string name, AccountType type, decimal openingBalance, bool countsAsAvailableCash, int sortOrder)
    {
        Id = id;
        Name = name;
        Type = type;
        OpeningBalance = openingBalance;
        CountsAsAvailableCash = countsAsAvailableCash;
        SortOrder = sortOrder;
    }

    public AccountId Id { get; private set; }
    public string Name { get; private set; }
    public AccountType Type { get; private set; }
    public decimal OpeningBalance { get; private set; }
    /// <summary>只有現金帳戶可能為 true（例如外幣現鈔為 false）。</summary>
    public bool CountsAsAvailableCash { get; private set; }
    public bool IsLiability => Type is AccountType.CreditCard or AccountType.Loan;

    /// <summary>同一組內的顯示順序，0 起算且連續（組的定義見 <see cref="Book"/>）。</summary>
    public int SortOrder { get; private set; }
    /// <summary>封存時間；封存的項目不再出現在新增交易的選項中，但既有資料不受影響。</summary>
    public DateTimeOffset? ArchivedAt { get; private set; }
    public bool IsArchived => ArchivedAt is not null;

    internal void Rename(string name) => Name = name;

    /// <summary>重複封存時保留第一次的時間。</summary>
    internal void Archive(DateTimeOffset at) => ArchivedAt ??= at;

    internal void Unarchive() => ArchivedAt = null;

    internal void SetCountsAsAvailableCash(bool value) => CountsAsAvailableCash = value;
}
