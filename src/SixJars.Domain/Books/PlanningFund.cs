using SixJars.Domain.Common;

namespace SixJars.Domain.Books;

/// <summary>財務規劃帳戶：指定用途的虛擬信封，不是帳戶（ADR 0003）。</summary>
public sealed class PlanningFund
{
    internal PlanningFund(PlanningFundId id, string name, decimal openingBalance, int sortOrder)
    {
        Id = id;
        Name = name;
        OpeningBalance = openingBalance;
        SortOrder = sortOrder;
    }

    public PlanningFundId Id { get; private set; }
    public string Name { get; private set; }
    public decimal OpeningBalance { get; private set; }

    /// <summary>同一組內的顯示順序，0 起算且連續（組的定義見 <see cref="Book"/>）。</summary>
    public int SortOrder { get; private set; }
    /// <summary>封存時間；封存的項目不再出現在新增交易的選項中，但既有資料不受影響。</summary>
    public DateTimeOffset? ArchivedAt { get; private set; }
    public bool IsArchived => ArchivedAt is not null;

    internal void Rename(string name) => Name = name;

    /// <summary>重複封存時保留第一次的時間。</summary>
    internal void Archive(DateTimeOffset at) => ArchivedAt ??= at;

    internal void Unarchive() => ArchivedAt = null;

    internal void MoveTo(int sortOrder) => SortOrder = sortOrder;
}
