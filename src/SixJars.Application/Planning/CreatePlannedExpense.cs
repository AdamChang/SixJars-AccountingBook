using FluentValidation;
using MediatR;
using SixJars.Application.Auditing;
using SixJars.Application.Books;
using SixJars.Application.Common;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;

namespace SixJars.Application.Planning;

/// <summary>新增一筆預定支出。形狀由 <see cref="PlannedExpenseInputValidator"/> 檢查（400），業務規則由 <see cref="PlannedExpense"/> 檢查（422）。</summary>
public sealed record CreatePlannedExpense(Guid BookId, PlannedExpenseInput Input) : IRequest<PlannedExpenseDto>, IBookScoped;

internal sealed class CreatePlannedExpenseValidator : AbstractValidator<CreatePlannedExpense>
{
    public CreatePlannedExpenseValidator() => RuleFor(c => c.Input).NotNull().SetValidator(new PlannedExpenseInputValidator());
}

internal sealed class CreatePlannedExpenseHandler(ISixJarsDbContext db, IAuditTrail audit) : IRequestHandler<CreatePlannedExpense, PlannedExpenseDto>
{
    public async Task<PlannedExpenseDto> Handle(CreatePlannedExpense request, CancellationToken cancellationToken)
    {
        // 帳本只用來驗證帳戶、分類與鎖帳日，不會被修改。
        var book = await db.GetBookAsNoTrackingAsync(request.BookId, cancellationToken);
        var input = request.Input;
        var budgetMonth = BudgetMonth.FromKey(input.BudgetMonth);
        book.EnsureUnlocked(budgetMonth);
        var planned = PlannedExpense.Create(
            book, budgetMonth, new CategoryId(input.CategoryId),
            input.AccountId is { } accountId ? new AccountId(accountId) : null, input.EstimatedAmount, input.Note);
        db.PlannedExpenses.Add(planned);
        audit.Record(request.BookId, AuditAction.Create, AuditEntityTypes.PlannedExpense, planned.Id.Value,
            null, PlannedExpenseDto.From(planned, AuditSnapshots.UnknownVersion));
        await db.SaveChangesAsync(cancellationToken);

        // 存檔時 EF 已讀回資料庫產生的版本。
        return PlannedExpenseDto.From(planned, db.GetVersion(planned));
    }
}
