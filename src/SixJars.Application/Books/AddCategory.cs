using FluentValidation;
using MediatR;
using SixJars.Application.Common;
using SixJars.Domain.Books;
using SixJars.Domain.Common;

namespace SixJars.Application.Books;

/// <summary>
/// 新增分類，回傳新分類的 Id。有 <see cref="ParentId"/> 時新增子分類（種類與性質沿用主分類，<see cref="Kind"/>、<see cref="Nature"/> 不使用）；
/// 沒有時新增主分類，支出主分類必須指定 <see cref="Nature"/>。
/// </summary>
public sealed record AddCategory(Guid BookId, string Name, CategoryKind Kind, ExpenseNature? Nature, Guid? ParentId) : IRequest<Guid>;

internal sealed class AddCategoryValidator : AbstractValidator<AddCategory>
{
    public AddCategoryValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Kind).IsInEnum();
        RuleFor(c => c.Nature).IsInEnum();
        RuleFor(c => c.Nature).NotNull()
            .When(c => c.ParentId is null && c.Kind == CategoryKind.Expense)
            .WithMessage("支出主分類必須指定支出性質。");
    }
}

internal sealed class AddCategoryHandler(ISixJarsDbContext db) : IRequestHandler<AddCategory, Guid>
{
    public async Task<Guid> Handle(AddCategory request, CancellationToken cancellationToken)
    {
        var book = await db.GetBookForUpdateAsync(request.BookId, cancellationToken);
        var category = request switch
        {
            { ParentId: { } parentId } => book.AddSubCategory(new CategoryId(parentId), request.Name),
            { Kind: CategoryKind.Income } => book.AddIncomeCategory(request.Name),
            _ => book.AddExpenseCategory(request.Name, request.Nature!.Value),
        };
        await db.SaveChangesAsync(cancellationToken);
        return category.Id.Value;
    }
}
