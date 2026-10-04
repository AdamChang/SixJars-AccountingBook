using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Auditing;
using SixJars.Application.Books;
using SixJars.Application.Common;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;

namespace SixJars.Application.Planning;

/// <summary>
/// 修改一筆未付的預定支出，以樂觀並行控制：<see cref="Version"/> 是前端讀到的版本，
/// 期間若有人改過（含付款），存檔時擲 <see cref="DbUpdateConcurrencyException"/>（409）。
/// </summary>
public sealed record UpdatePlannedExpense(Guid BookId, Guid PlannedExpenseId, uint Version, PlannedExpenseInput Input) : IRequest<PlannedExpenseDto>;

internal sealed class UpdatePlannedExpenseValidator : AbstractValidator<UpdatePlannedExpense>
{
    public UpdatePlannedExpenseValidator() => RuleFor(c => c.Input).NotNull().SetValidator(new PlannedExpenseInputValidator());
}

internal sealed class UpdatePlannedExpenseHandler(ISixJarsDbContext db, IAuditTrail audit) : IRequestHandler<UpdatePlannedExpense, PlannedExpenseDto>
{
    public async Task<PlannedExpenseDto> Handle(UpdatePlannedExpense request, CancellationToken cancellationToken)
    {
        var planned = await db.FindPlannedExpenseAsync(request.BookId, request.PlannedExpenseId, cancellationToken);

        // 帳本只用來驗證帳戶與分類，不會被修改。
        var book = await db.GetBookAsNoTrackingAsync(request.BookId, cancellationToken);
        var input = request.Input;

        // 修改前的快照必須在 Update 之前取得，否則會拿到修改後的內容。
        var before = PlannedExpenseDto.From(planned, db.GetVersion(planned));
        db.ExpectVersion(planned, request.Version);
        planned.Update(
            book, BudgetMonth.FromKey(input.BudgetMonth), new CategoryId(input.CategoryId),
            input.AccountId is { } accountId ? new AccountId(accountId) : null, input.EstimatedAmount, input.Note);
        audit.Record(request.BookId, AuditAction.Update, AuditEntityTypes.PlannedExpense, planned.Id.Value,
            before, PlannedExpenseDto.From(planned, AuditSnapshots.UnknownVersion));
        await db.SaveChangesAsync(cancellationToken);

        return PlannedExpenseDto.From(planned, db.GetVersion(planned));
    }
}

internal static class PlannedExpenseLoading
{
    /// <summary>
    /// 載入要修改的預定支出（追蹤變更，db.GetVersion 才讀得到版本）；找不到時擲 <see cref="NotFoundException"/>。
    /// 帳本與預定支出 Id 一起當查詢條件：只用預定支出 Id 查詢，就能改到別本帳的預定支出。
    /// </summary>
    public static async Task<PlannedExpense> FindPlannedExpenseAsync(
        this ISixJarsDbContext db, Guid bookId, Guid plannedExpenseId, CancellationToken cancellationToken)
    {
        var book = new BookId(bookId);
        var id = new PlannedExpenseId(plannedExpenseId);
        return await db.PlannedExpenses.SingleOrDefaultAsync(p => p.BookId == book && p.Id == id, cancellationToken)
            ?? throw new NotFoundException($"找不到預定支出 {plannedExpenseId}。");
    }
}
