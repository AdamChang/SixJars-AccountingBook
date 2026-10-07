using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Auditing;
using SixJars.Application.Books;
using SixJars.Application.Common;
using SixJars.Application.Planning;
using SixJars.Application.Transactions;
using SixJars.Domain.Common;

namespace SixJars.Application.Backup;

/// <summary>
/// 匯出一本帳的完整備份（CONTEXT.md「備份」），<b>包含已軟刪除的資料</b>；還原用 CLI 的 <c>restore-backup</c>（T42）。
/// 每個清單都有固定的排序，同一份資料每次匯出的結果相同（往返測試依賴這一點）。
/// </summary>
public sealed record ExportBackup(Guid BookId) : IRequest<BackupDocument>, IBookScoped;

internal sealed class ExportBackupHandler(ISixJarsDbContext db, TimeProvider clock) : IRequestHandler<ExportBackup, BackupDocument>
{
    public async Task<BackupDocument> Handle(ExportBackup request, CancellationToken cancellationToken)
    {
        var bookId = new BookId(request.BookId);
        var book = await db.GetBookAsNoTrackingAsync(request.BookId, cancellationToken);

        // IgnoreQueryFilters 才讀得到已刪除的資料；它會拿掉實體上的所有 filter，所以 BookId 條件一律寫在查詢裡，
        // 不可省略，否則別本帳的資料會混進備份。
        // 交易與預定支出用追蹤查詢，才能經由 GetVersion 讀到 xmin（同 ListTransactions）。
        var transactions = await db.Transactions.IgnoreQueryFilters()
            .Where(t => t.BookId == bookId)
            .OrderBy(t => t.Date).ThenBy(t => t.Id)
            .ToListAsync(cancellationToken);
        var plannedExpenses = await db.PlannedExpenses.IgnoreQueryFilters()
            .Where(p => p.BookId == bookId)
            .OrderBy(p => p.BudgetMonth).ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);
        // 週期項目沒有軟刪除；同樣用追蹤查詢讀 xmin。
        var recurring = await db.RecurringPlannedExpenses
            .Where(r => r.BookId == bookId)
            .OrderBy(r => r.Id)
            .ToListAsync(cancellationToken);
        var members = await db.BookMembers.AsNoTracking()
            .Where(m => m.BookId == bookId)
            .OrderBy(m => m.AddedAt).ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);
        // 同一次 SaveChanges 的記錄 At 相同，再依 Id（UUIDv7）排序讓結果穩定（同 GetAuditHistory）。
        var auditEntries = await db.AuditEntries.AsNoTracking()
            .Where(e => e.BookId == request.BookId)
            .OrderBy(e => e.At).ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);

        return new BackupDocument(
            BackupDocument.CurrentFormatVersion,
            clock.GetUtcNow(),
            BookDto.From(book),
            [.. transactions.Select(t => new BackupTransaction(TransactionDto.From(t, db.GetVersion(t)), t.DeletedAt))],
            [.. plannedExpenses.Select(p => new BackupPlannedExpense(PlannedExpenseDto.From(p, db.GetVersion(p)), p.DeletedAt))],
            [.. members.Select(m => new BackupMember(m.Id, m.Email, m.GoogleSubject, m.Role, m.AddedAt))],
            [.. auditEntries.Select(AuditEntryDto.From)],
            [.. recurring.Select(r => RecurringPlannedExpenseDto.From(r, db.GetVersion(r)))]);
    }
}
