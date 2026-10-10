using SixJars.Domain.Books;
using SixJars.Domain.Common;

namespace SixJars.Domain.Planning;

public enum BudgetSource
{
    Default,
    Override,
}

/// <summary>某個歸屬月份的覆寫值（spec §5.1）；是 <see cref="CategoryBudget"/> 的一部分，只能經由它修改。</summary>
public sealed class BudgetOverride
{
    // EF Core 用。
    private BudgetOverride()
    {
    }

    internal BudgetOverride(BudgetMonth month, decimal amount)
    {
        Month = month;
        Amount = amount;
    }

    public BudgetMonth Month { get; private set; }
    public decimal Amount { get; private set; }

    internal void Change(decimal amount) => Amount = amount;
}

/// <summary>
/// 一個浮動支出主分類的預算（CONTEXT.md「預算」、spec §5.1）：預設值＋個別歸屬月份的覆寫值，以正數表示上限，未用完不累積。
/// 不放進 <see cref="Book"/>：覆寫值會隨月份累積，生命週期與帳本設定不同。主鍵就是分類 Id（P4 L plan Q6）。
/// </summary>
public sealed class CategoryBudget
{
    private readonly List<BudgetOverride> _overrides = [];

    // EF Core 用。
    private CategoryBudget()
    {
    }

    public CategoryId CategoryId { get; private set; }
    public BookId BookId { get; private set; }
    public decimal? DefaultAmount { get; private set; }
    /// <summary>順序不保證；需要穩定順序時（快照、備份）依月份排序。</summary>
    public IReadOnlyList<BudgetOverride> Overrides => _overrides;
    /// <summary>預設值與覆寫值都沒有；這時整筆刪除（P4 L plan Q1c）。</summary>
    public bool IsEmpty => DefaultAmount is null && _overrides.Count == 0;

    /// <summary>
    /// 只在建立時驗證分類：建立之後該分類不能改性質、不能刪除（<see cref="Book.ChangeExpenseNature"/>、<see cref="Book.RemoveCategory"/>），
    /// 所以設定金額時不必再驗一次。已封存的分類可以有預算（P4 L plan D4）。
    /// </summary>
    public static CategoryBudget Create(Book book, CategoryId categoryId)
    {
        var category = book.GetCategory(categoryId);
        if (!category.IsMain || category.Kind != CategoryKind.Expense || category.Nature != ExpenseNature.Floating)
        {
            throw new DomainException($"預算只限浮動支出的主分類，「{category.Name}」不是。");
        }

        return new CategoryBudget { CategoryId = categoryId, BookId = book.Id };
    }

    public void SetDefault(decimal amount) => DefaultAmount = EnsureNotNegative(amount);

    /// <returns>沒有預設值時回傳 false，不改變任何東西。</returns>
    public bool RemoveDefault()
    {
        if (DefaultAmount is null)
        {
            return false;
        }

        DefaultAmount = null;
        return true;
    }

    /// <summary>同一個月份已有覆寫值時原地改金額（EF 對應成 UPDATE）。</summary>
    public void SetOverride(BudgetMonth month, decimal amount)
    {
        EnsureNotNegative(amount);
        var existing = _overrides.Find(o => o.Month == month);
        if (existing is null)
        {
            _overrides.Add(new BudgetOverride(month, amount));
        }
        else
        {
            existing.Change(amount);
        }
    }

    /// <returns>該月沒有覆寫值時回傳 false。</returns>
    public bool RemoveOverride(BudgetMonth month) => _overrides.RemoveAll(o => o.Month == month) > 0;

    public bool HasOverride(BudgetMonth month) => _overrides.Exists(o => o.Month == month);

    /// <summary>該月適用的預算：覆寫值優先，其次預設值；都沒有時兩者皆為 null（P4 L plan Q1a）。</summary>
    public (decimal? Amount, BudgetSource? Source) AmountFor(BudgetMonth month)
    {
        if (_overrides.Find(o => o.Month == month) is { } monthOverride)
        {
            return (monthOverride.Amount, BudgetSource.Override);
        }

        if (DefaultAmount is { } defaultAmount)
        {
            return (defaultAmount, BudgetSource.Default);
        }

        return (null, null);
    }

    private static decimal EnsureNotNegative(decimal amount) =>
        amount >= 0m ? amount : throw new DomainException($"預算金額不能是負數（{amount}）。");
}
