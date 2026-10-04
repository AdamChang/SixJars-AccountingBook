using SixJars.Domain.Books;

namespace SixJars.Application.Books;

/// <summary>帳本設定：帳戶、財務規劃帳戶與分類（分類為扁平清單，子分類以 <see cref="CategoryDto.ParentId"/> 指向主分類）。</summary>
public sealed record BookDto(Guid Id, string Name, DateOnly OpeningDate, DateOnly? LockDate,
    IReadOnlyList<AccountDto> Accounts, IReadOnlyList<PlanningFundDto> PlanningFunds, IReadOnlyList<CategoryDto> Categories)
{
    public static BookDto From(Book book) => new(
        book.Id.Value,
        book.Name,
        book.OpeningDate,
        book.LockDate,
        [.. book.Accounts.Select(AccountDto.From)],
        [.. book.PlanningFunds.Select(PlanningFundDto.From)],
        [.. book.Categories.Select(CategoryDto.From)]);
}

public sealed record AccountDto(Guid Id, string Name, AccountType Type, decimal OpeningBalance, bool CountsAsAvailableCash)
{
    public static AccountDto From(Account a) => new(a.Id.Value, a.Name, a.Type, a.OpeningBalance, a.CountsAsAvailableCash);
}

public sealed record PlanningFundDto(Guid Id, string Name, decimal OpeningBalance)
{
    public static PlanningFundDto From(PlanningFund f) => new(f.Id.Value, f.Name, f.OpeningBalance);
}

public sealed record CategoryDto(Guid Id, string Name, CategoryKind Kind, ExpenseNature? Nature, Guid? ParentId)
{
    public static CategoryDto From(Category c) => new(c.Id.Value, c.Name, c.Kind, c.Nature, c.ParentId?.Value);
}

public sealed record BookSummaryDto(Guid Id, string Name);
