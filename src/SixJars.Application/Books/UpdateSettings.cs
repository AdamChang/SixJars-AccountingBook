using FluentValidation;
using MediatR;
using SixJars.Application.Auditing;
using SixJars.Application.Common;
using SixJars.Domain.Books;
using SixJars.Domain.Common;

namespace SixJars.Application.Books;

public sealed record UpdateAccount(Guid BookId, Guid AccountId, string Name, bool CountsAsAvailableCash) : IRequest, IBookScoped;

public sealed record UpdatePlanningFund(Guid BookId, Guid PlanningFundId, string Name) : IRequest, IBookScoped;

/// <summary><see cref="Nature"/> 為 null 時不修改性質；只有支出主分類可以修改（回溯生效，spec Q4）。</summary>
public sealed record UpdateCategory(Guid BookId, Guid CategoryId, string Name, ExpenseNature? Nature) : IRequest, IBookScoped;

internal sealed class UpdateAccountValidator : AbstractValidator<UpdateAccount>
{
    public UpdateAccountValidator() => RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
}

internal sealed class UpdatePlanningFundValidator : AbstractValidator<UpdatePlanningFund>
{
    public UpdatePlanningFundValidator() => RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
}

internal sealed class UpdateCategoryValidator : AbstractValidator<UpdateCategory>
{
    public UpdateCategoryValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Nature).IsInEnum();
    }
}

internal sealed class UpdateAccountHandler(ISixJarsDbContext db, IAuditTrail audit) : IRequestHandler<UpdateAccount>
{
    public async Task Handle(UpdateAccount request, CancellationToken cancellationToken)
    {
        var book = await db.GetBookForUpdateAsync(request.BookId, cancellationToken);
        var id = new AccountId(request.AccountId);
        var before = AccountDto.From(book.GetAccount(id));
        book.RenameAccount(id, request.Name);
        book.SetCountsAsAvailableCash(id, request.CountsAsAvailableCash);
        audit.Record(request.BookId, AuditAction.Update, AuditEntityTypes.Account, id.Value, before, AccountDto.From(book.GetAccount(id)));
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class UpdatePlanningFundHandler(ISixJarsDbContext db, IAuditTrail audit) : IRequestHandler<UpdatePlanningFund>
{
    public async Task Handle(UpdatePlanningFund request, CancellationToken cancellationToken)
    {
        var book = await db.GetBookForUpdateAsync(request.BookId, cancellationToken);
        var id = new PlanningFundId(request.PlanningFundId);
        var before = PlanningFundDto.From(book.GetPlanningFund(id));
        book.RenamePlanningFund(id, request.Name);
        audit.Record(request.BookId, AuditAction.Update, AuditEntityTypes.PlanningFund, id.Value, before, PlanningFundDto.From(book.GetPlanningFund(id)));
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class UpdateCategoryHandler(ISixJarsDbContext db, IAuditTrail audit) : IRequestHandler<UpdateCategory>
{
    public async Task Handle(UpdateCategory request, CancellationToken cancellationToken)
    {
        var book = await db.GetBookForUpdateAsync(request.BookId, cancellationToken);
        var id = new CategoryId(request.CategoryId);
        var category = book.GetCategory(id);
        var before = CategoryDto.From(category);
        book.RenameCategory(id, request.Name);
        if (request.Nature is { } nature && nature != category.Nature)
        {
            var hasPlannedExpenses = nature == ExpenseNature.Floating
                && await db.HasPlannedExpensesAsync(book, id, cancellationToken);
            var hasRecurring = nature is not (ExpenseNature.Fixed or ExpenseNature.Loan)
                && await db.HasRecurringPlannedExpensesAsync(book, id, cancellationToken);
            // 預算只在浮動主分類上；其他分類不必查（P4 L plan L7）。
            var hasBudget = category is { IsMain: true, Nature: ExpenseNature.Floating }
                && await db.HasBudgetAsync(book.Id, id, cancellationToken);
            book.ChangeExpenseNature(id, nature, hasPlannedExpenses, hasRecurring, hasBudget);
        }

        audit.Record(request.BookId, AuditAction.Update, AuditEntityTypes.Category, id.Value, before, CategoryDto.From(category));
        await db.SaveChangesAsync(cancellationToken);
    }
}
