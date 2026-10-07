using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Auditing;
using SixJars.Application.Books;
using SixJars.Application.Common;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;

namespace SixJars.Application.Planning;

/// <summary>週期預定支出清單，依建立順序。包含已結束的（前端自行標示）。</summary>
public sealed record ListRecurringPlannedExpenses(Guid BookId) : IRequest<IReadOnlyList<RecurringPlannedExpenseDto>>, IBookScoped;

public sealed record CreateRecurringPlannedExpense(Guid BookId, RecurringPlannedExpenseInput Input)
    : IRequest<RecurringPlannedExpenseDto>, IBookScoped;

/// <param name="Version">讀取時拿到的版本；期間有人改過就是 409（P4 K plan D9）。</param>
public sealed record UpdateRecurringPlannedExpense(Guid BookId, Guid RecurringPlannedExpenseId, uint Version, RecurringPlannedExpenseInput Input)
    : IRequest<RecurringPlannedExpenseDto>, IBookScoped;

internal sealed class CreateRecurringPlannedExpenseValidator : AbstractValidator<CreateRecurringPlannedExpense>
{
    public CreateRecurringPlannedExpenseValidator() =>
        RuleFor(c => c.Input).NotNull().SetValidator(new RecurringPlannedExpenseInputValidator());
}

internal sealed class UpdateRecurringPlannedExpenseValidator : AbstractValidator<UpdateRecurringPlannedExpense>
{
    public UpdateRecurringPlannedExpenseValidator() =>
        RuleFor(c => c.Input).NotNull().SetValidator(new RecurringPlannedExpenseInputValidator());
}

internal sealed class ListRecurringPlannedExpensesHandler(ISixJarsDbContext db)
    : IRequestHandler<ListRecurringPlannedExpenses, IReadOnlyList<RecurringPlannedExpenseDto>>
{
    public async Task<IReadOnlyList<RecurringPlannedExpenseDto>> Handle(ListRecurringPlannedExpenses request, CancellationToken cancellationToken)
    {
        var bookId = new BookId(request.BookId);
        // 要追蹤變更，db.GetVersion 才讀得到版本（同 ListPlannedExpenses）。
        var items = await db.RecurringPlannedExpenses.Where(r => r.BookId == bookId).OrderBy(r => r.Id).ToListAsync(cancellationToken);
        return [.. items.Select(r => RecurringPlannedExpenseDto.From(r, db.GetVersion(r)))];
    }
}

internal sealed class CreateRecurringPlannedExpenseHandler(ISixJarsDbContext db, IAuditTrail audit)
    : IRequestHandler<CreateRecurringPlannedExpense, RecurringPlannedExpenseDto>
{
    public async Task<RecurringPlannedExpenseDto> Handle(CreateRecurringPlannedExpense request, CancellationToken cancellationToken)
    {
        // 帳本只用來驗證分類與帳戶，不會被修改。週期項目本身不影響金額，所以不檢查鎖帳日。
        var book = await db.GetBookAsNoTrackingAsync(request.BookId, cancellationToken);
        var input = request.Input;
        var item = RecurringPlannedExpense.Create(
            book, new CategoryId(input.CategoryId), input.AccountId is { } a ? new AccountId(a) : null, input.DefaultAmount, input.Note,
            input.Frequency, input.Months, BudgetMonth.FromKey(input.StartMonth), input.EndMonth is { } e ? BudgetMonth.FromKey(e) : null);
        db.RecurringPlannedExpenses.Add(item);
        audit.Record(request.BookId, AuditAction.Create, AuditEntityTypes.RecurringPlannedExpense, item.Id.Value,
            null, RecurringPlannedExpenseDto.From(item, AuditSnapshots.UnknownVersion));
        await db.SaveChangesAsync(cancellationToken);
        return RecurringPlannedExpenseDto.From(item, db.GetVersion(item));
    }
}

internal sealed class UpdateRecurringPlannedExpenseHandler(ISixJarsDbContext db, IAuditTrail audit)
    : IRequestHandler<UpdateRecurringPlannedExpense, RecurringPlannedExpenseDto>
{
    public async Task<RecurringPlannedExpenseDto> Handle(UpdateRecurringPlannedExpense request, CancellationToken cancellationToken)
    {
        var item = await db.FindRecurringPlannedExpenseAsync(request.BookId, request.RecurringPlannedExpenseId, cancellationToken);
        var book = await db.GetBookAsNoTrackingAsync(request.BookId, cancellationToken);
        var input = request.Input;

        var before = RecurringPlannedExpenseDto.From(item, db.GetVersion(item));
        db.ExpectVersion(item, request.Version);
        item.Update(
            book, new CategoryId(input.CategoryId), input.AccountId is { } a ? new AccountId(a) : null, input.DefaultAmount, input.Note,
            input.Frequency, input.Months, BudgetMonth.FromKey(input.StartMonth), input.EndMonth is { } e ? BudgetMonth.FromKey(e) : null);
        audit.Record(request.BookId, AuditAction.Update, AuditEntityTypes.RecurringPlannedExpense, item.Id.Value,
            before, RecurringPlannedExpenseDto.From(item, AuditSnapshots.UnknownVersion));
        await db.SaveChangesAsync(cancellationToken);
        return RecurringPlannedExpenseDto.From(item, db.GetVersion(item));
    }
}

internal static class RecurringPlannedExpenseLoading
{
    /// <summary>帳本與 Id 一起當查詢條件（同 FindPlannedExpenseAsync）；找不到擲 <see cref="NotFoundException"/>。</summary>
    public static async Task<RecurringPlannedExpense> FindRecurringPlannedExpenseAsync(
        this ISixJarsDbContext db, Guid bookId, Guid recurringId, CancellationToken cancellationToken)
    {
        var book = new BookId(bookId);
        var id = new RecurringPlannedExpenseId(recurringId);
        return await db.RecurringPlannedExpenses.SingleOrDefaultAsync(r => r.BookId == book && r.Id == id, cancellationToken)
            ?? throw new NotFoundException($"找不到週期預定支出 {recurringId}。");
    }
}
