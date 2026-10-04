using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Auditing;
using SixJars.Application.Books;
using SixJars.Application.Common;

namespace SixJars.Application.Planning;

/// <summary>
/// 軟刪除一筆預定支出（spec §3.3），以樂觀並行控制：<see cref="Version"/> 是前端讀到的版本，
/// 期間若有人改過（含付款），存檔時擲 <see cref="DbUpdateConcurrencyException"/>（409）。
/// 已付款的也可以刪除：刪除的是計畫本身，付款交易保留。
/// </summary>
public sealed record DeletePlannedExpense(Guid BookId, Guid PlannedExpenseId, uint Version) : IRequest;

internal sealed class DeletePlannedExpenseHandler(ISixJarsDbContext db, TimeProvider clock, IAuditTrail audit) : IRequestHandler<DeletePlannedExpense>
{
    public async Task Handle(DeletePlannedExpense request, CancellationToken cancellationToken)
    {
        // 已刪除的預定支出被 query filter 擋掉，同樣 404。
        var planned = await db.FindPlannedExpenseAsync(request.BookId, request.PlannedExpenseId, cancellationToken);

        // 帳本只用來檢查鎖帳日，不會被修改。
        var book = await db.GetBookAsNoTrackingAsync(request.BookId, cancellationToken);
        book.EnsureUnlocked(planned.BudgetMonth);

        // 快照在 Delete 之前取得；DeletedAt 不在 DTO 裡，刪除時間就是稽核記錄的 At。
        var before = PlannedExpenseDto.From(planned, db.GetVersion(planned));
        db.ExpectVersion(planned, request.Version);
        planned.Delete(clock.GetUtcNow());
        audit.Record<PlannedExpenseDto>(request.BookId, AuditAction.Delete, AuditEntityTypes.PlannedExpense, planned.Id.Value, before, null);
        await db.SaveChangesAsync(cancellationToken);
    }
}
