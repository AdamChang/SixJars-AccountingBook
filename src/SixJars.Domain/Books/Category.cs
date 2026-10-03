using SixJars.Domain.Common;

namespace SixJars.Domain.Books;

/// <summary>兩層報表歸類（主分類／子分類），只掛在收入與支出類交易上。</summary>
public sealed class Category
{
    internal Category(CategoryId id, string name, CategoryKind kind, ExpenseNature? nature, CategoryId? parentId)
    {
        Id = id;
        Name = name;
        Kind = kind;
        Nature = nature;
        ParentId = parentId;
    }

    public CategoryId Id { get; private set; }
    public string Name { get; private set; }
    public CategoryKind Kind { get; private set; }
    /// <summary>支出性質；收入分類為 null。</summary>
    public ExpenseNature? Nature { get; private set; }
    public CategoryId? ParentId { get; private set; }
    public bool IsMain => ParentId is null;
}
