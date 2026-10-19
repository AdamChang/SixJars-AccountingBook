using SixJars.Domain.Common;

namespace SixJars.Domain.Books;

/// <summary>標籤：帳本的設定項目，可複選掛在任何交易上，與分類互相獨立（ADR 0011）。</summary>
public sealed class Tag
{
    /// <summary>名稱上限，以 text element 計算（一個 emoji 算 1 個字，spec G5、D8）。</summary>
    public const int MaxNameLength = 8;

    internal Tag(TagId id, string name, TagColor color, int sortOrder)
    {
        Id = id;
        Name = name;
        Color = color;
        SortOrder = sortOrder;
    }

    public TagId Id { get; private set; }
    public string Name { get; private set; }
    public TagColor Color { get; private set; }

    /// <summary>顯示順序，0 起算且連續；交易上的標籤也依此排列（spec G20）。</summary>
    public int SortOrder { get; private set; }
    /// <summary>封存時間；封存的標籤不能再新加到交易上，但既有交易保留（spec G7）。</summary>
    public DateTimeOffset? ArchivedAt { get; private set; }
    public bool IsArchived => ArchivedAt is not null;

    internal void Rename(string name) => Name = name;

    internal void Recolor(TagColor color) => Color = color;

    /// <summary>重複封存時保留第一次的時間。</summary>
    internal void Archive(DateTimeOffset at) => ArchivedAt ??= at;

    internal void Unarchive() => ArchivedAt = null;

    internal void MoveTo(int sortOrder) => SortOrder = sortOrder;
}
