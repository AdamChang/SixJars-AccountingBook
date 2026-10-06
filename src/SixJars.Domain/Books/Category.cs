using SixJars.Domain.Common;

namespace SixJars.Domain.Books;

/// <summary>兩層報表歸類（主分類／子分類），只掛在收入與支出類交易上。</summary>
public sealed class Category
{
    internal Category(CategoryId id, string name, CategoryKind kind, ExpenseNature? nature, CategoryId? parentId, int sortOrder)
    {
        Id = id;
        Name = name;
        Kind = kind;
        Nature = nature;
        ParentId = parentId;
        SortOrder = sortOrder;
    }

    public CategoryId Id { get; private set; }
    public string Name { get; private set; }
    public CategoryKind Kind { get; private set; }
    /// <summary>支出性質；收入分類為 null。</summary>
    public ExpenseNature? Nature { get; private set; }
    public CategoryId? ParentId { get; private set; }
    public bool IsMain => ParentId is null;

    /// <summary>同一組內的顯示順序，0 起算且連續（組的定義見 <see cref="Book"/>）。</summary>
    public int SortOrder { get; private set; }
    /// <summary>封存時間；封存的項目不再出現在新增交易的選項中，但既有資料不受影響。</summary>
    public DateTimeOffset? ArchivedAt { get; private set; }
    public bool IsArchived => ArchivedAt is not null;
}
