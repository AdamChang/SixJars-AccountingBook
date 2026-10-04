using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Common;

namespace SixJars.Application.Planning;

/// <summary>
/// 軟刪除一筆預定支出（spec §3.3），以樂觀並行控制：<see cref="Version"/> 是前端讀到的版本，
/// 期間若有人改過（含付款），存檔時擲 <see cref="DbUpdateConcurrencyException"/>（409）。
/// 已付款的也可以刪除：刪除的是計畫本身，付款交易保留。
/// </summary>
public sealed record DeletePlannedExpense(Guid BookId, Guid PlannedExpenseId, uint Version) : IRequest;

internal sealed class DeletePlannedExpenseHandler(ISixJarsDbContext db, TimeProvider clock) : IRequestHandler<DeletePlannedExpense>
{
    public async Task Handle(DeletePlannedExpense request, CancellationToken cancellationToken)
    {
        // 已刪除的預定支出被 query filter 擋掉，同樣 404。
        var planned = await db.FindPlannedExpenseAsync(request.BookId, request.PlannedExpenseId, cancellationToken);

        db.ExpectVersion(planned, request.Version);
        planned.Delete(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
    }
}
