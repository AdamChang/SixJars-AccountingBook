using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Auditing;
using SixJars.Application.Common;
using SixJars.Application.Planning;
using SixJars.Domain.Common;

namespace SixJars.Application.Transactions;

/// <summary>
/// 軟刪除一筆交易（spec §3.3），以樂觀並行控制：<see cref="Version"/> 是前端讀到的版本，
/// 期間若有人改過這筆交易，存檔時擲 <see cref="DbUpdateConcurrencyException"/>（409）。
/// 這筆交易若是某預定支出的付款，該預定支出一併回到未付（spec §9 O2）。
/// </summary>
public sealed record DeleteTransaction(Guid BookId, Guid TransactionId, uint Version) : IRequest;

internal sealed class DeleteTransactionHandler(ISixJarsDbContext db, TimeProvider clock, IAuditTrail audit) : IRequestHandler<DeleteTransaction>
{
    public async Task Handle(DeleteTransaction request, CancellationToken cancellationToken)
    {
        // 帳本與交易 Id 一起當查詢條件：只用交易 Id 查詢，就能刪到別本帳的交易。已刪除的交易被 query filter 擋掉，同樣 404。
        var bookId = new BookId(request.BookId);
        var transactionId = new TransactionId(request.TransactionId);
        var transaction = await db.Transactions
            .SingleOrDefaultAsync(t => t.BookId == bookId && t.Id == transactionId, cancellationToken)
            ?? throw new NotFoundException($"找不到交易 {request.TransactionId}。");

        // 快照在 Delete 之前取得；DeletedAt 不在 DTO 裡，刪除時間就是稽核記錄的 At。
        var before = TransactionDto.From(transaction, db.GetVersion(transaction));
        db.ExpectVersion(transaction, request.Version);
        transaction.Delete(clock.GetUtcNow());
        audit.Record<TransactionDto>(request.BookId, AuditAction.Delete, AuditEntityTypes.Transaction, transaction.Id.Value, before, null);

        // 已刪除的預定支出被 query filter 擋掉，不會在這裡解除連結（已刪除的資料不能修改）。
        var paidPlans = await db.PlannedExpenses
            .Where(p => p.BookId == bookId && p.PaidTransactionId == transactionId)
            .ToListAsync(cancellationToken);
        foreach (var planned in paidPlans)
        {
            // 解除連結也是對預定支出的修改，另外留一筆 Update。
            var plannedBefore = PlannedExpenseDto.From(planned, db.GetVersion(planned));
            planned.MarkUnpaid();
            audit.Record(request.BookId, AuditAction.Update, AuditEntityTypes.PlannedExpense, planned.Id.Value,
                plannedBefore, PlannedExpenseDto.From(planned, AuditSnapshots.UnknownVersion));
        }

        // 刪除交易、解除付款連結與稽核記錄在同一次 SaveChanges，一起成功或一起失敗。
        await db.SaveChangesAsync(cancellationToken);
    }
}
