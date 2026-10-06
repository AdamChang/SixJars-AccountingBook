using SixJars.Domain.Books;

namespace SixJars.Application.Books;

/// <summary>
/// 帳本設定：帳戶、財務規劃帳戶與分類，各依 SortOrder 輸出。
/// 分類為扁平清單（子分類以 <see cref="CategoryDto.ParentId"/> 指向主分類），順序是樹的前序。
/// </summary>
public sealed record BookDto(Guid Id, string Name, DateOnly OpeningDate, DateOnly? LockDate,
    IReadOnlyList<AccountDto> Accounts, IReadOnlyList<PlanningFundDto> PlanningFunds, IReadOnlyList<CategoryDto> Categories)
{
    public static BookDto From(Book book) => new(
        book.Id.Value,
        book.Name,
        book.OpeningDate,
        book.LockDate,
        [.. book.Accounts.OrderBy(a => a.SortOrder).Select(AccountDto.From)],
        [.. book.PlanningFunds.OrderBy(f => f.SortOrder).Select(PlanningFundDto.From)],
        [.. InTreeOrder(book.Categories).Select(CategoryDto.From)]);

    /// <summary>收入主分類、支出主分類各依 SortOrder；每個主分類後面接它的子分類（記帳頁的下拉選單直接用這個順序）。</summary>
    private static IEnumerable<Category> InTreeOrder(IReadOnlyList<Category> categories) =>
        categories.Where(c => c.IsMain).OrderBy(c => c.Kind).ThenBy(c => c.SortOrder)
            .SelectMany(main => categories.Where(c => c.ParentId == main.Id).OrderBy(c => c.SortOrder).Prepend(main));
}

// SortOrder、ArchivedAt 是選用參數：P3 的 v1 備份沒有這兩個欄位，BackupJson 又要求必填的建構子參數（P4 J plan D8）。
public sealed record AccountDto(Guid Id, string Name, AccountType Type, decimal OpeningBalance, bool CountsAsAvailableCash,
    int SortOrder = 0, DateTimeOffset? ArchivedAt = null)
{
    public static AccountDto From(Account a) =>
        new(a.Id.Value, a.Name, a.Type, a.OpeningBalance, a.CountsAsAvailableCash, a.SortOrder, a.ArchivedAt);
}

public sealed record PlanningFundDto(Guid Id, string Name, decimal OpeningBalance, int SortOrder = 0, DateTimeOffset? ArchivedAt = null)
{
    public static PlanningFundDto From(PlanningFund f) => new(f.Id.Value, f.Name, f.OpeningBalance, f.SortOrder, f.ArchivedAt);
}

public sealed record CategoryDto(Guid Id, string Name, CategoryKind Kind, ExpenseNature? Nature, Guid? ParentId,
    int SortOrder = 0, DateTimeOffset? ArchivedAt = null)
{
    public static CategoryDto From(Category c) =>
        new(c.Id.Value, c.Name, c.Kind, c.Nature, c.ParentId?.Value, c.SortOrder, c.ArchivedAt);
}

public sealed record BookSummaryDto(Guid Id, string Name);
