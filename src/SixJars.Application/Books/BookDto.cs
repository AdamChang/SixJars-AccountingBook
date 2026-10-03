using SixJars.Domain.Books;

namespace SixJars.Application.Books;

/// <summary>帳本設定：帳戶、財務規劃帳戶與分類（分類為扁平清單，子分類以 <see cref="CategoryDto.ParentId"/> 指向主分類）。</summary>
public sealed record BookDto(Guid Id, string Name, DateOnly OpeningDate, DateOnly? LockDate,
    IReadOnlyList<AccountDto> Accounts, IReadOnlyList<PlanningFundDto> PlanningFunds, IReadOnlyList<CategoryDto> Categories)
{
    // LockDate 在 T32 之前一律為 null，T32 再接上 Book.LockDate。
    public static BookDto From(Book book) => new(
        book.Id.Value,
        book.Name,
        book.OpeningDate,
        LockDate: null,
        [.. book.Accounts.Select(a => new AccountDto(a.Id.Value, a.Name, a.Type, a.OpeningBalance, a.CountsAsAvailableCash))],
        [.. book.PlanningFunds.Select(f => new PlanningFundDto(f.Id.Value, f.Name, f.OpeningBalance))],
        [.. book.Categories.Select(c => new CategoryDto(c.Id.Value, c.Name, c.Kind, c.Nature, c.ParentId?.Value))]);
}

public sealed record AccountDto(Guid Id, string Name, AccountType Type, decimal OpeningBalance, bool CountsAsAvailableCash);

public sealed record PlanningFundDto(Guid Id, string Name, decimal OpeningBalance);

public sealed record CategoryDto(Guid Id, string Name, CategoryKind Kind, ExpenseNature? Nature, Guid? ParentId);

public sealed record BookSummaryDto(Guid Id, string Name);
