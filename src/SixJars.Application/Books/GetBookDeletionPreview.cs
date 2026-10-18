using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Common;
using SixJars.Domain.Books;

namespace SixJars.Application.Books;

/// <summary>刪除帳本前的統計（issue #6 Q9）。同一組數字也是刪除稽核記錄的 Before（spec D4）。</summary>
public sealed record GetBookDeletionPreview(Guid BookId) : IRequest<BookDeletionPreview>, IBookScoped;

/// <param name="Transactions">未刪除的交易筆數（spec D3）；已軟刪除的交易一樣會被刪除，但不列入。</param>
/// <param name="PlannedExpenses">未刪除的預定支出筆數（同上）。</param>
/// <param name="AuditEntries">這本帳本的全部稽核記錄筆數；刪除後只剩一筆刪除摘要。</param>
public sealed record BookDeletionPreview(
    string Name, DateOnly OpeningDate, DateOnly? LockDate,
    int Transactions, int PlannedExpenses, int RecurringPlannedExpenses,
    int CategoryBudgets, int Members, int AuditEntries);

internal sealed class GetBookDeletionPreviewHandler(ISixJarsDbContext db)
    : IRequestHandler<GetBookDeletionPreview, BookDeletionPreview>
{
    public async Task<BookDeletionPreview> Handle(GetBookDeletionPreview request, CancellationToken cancellationToken) =>
        await BookDeletionPreviews.CreateAsync(db, await db.GetBookAsNoTrackingAsync(request.BookId, cancellationToken), cancellationToken);
}

internal static class BookDeletionPreviews
{
    /// <summary>交易與預定支出套用 query filter（只算未刪除的，D3）。依序 await：同一個 DbContext 不能並行查詢。</summary>
    public static async Task<BookDeletionPreview> CreateAsync(ISixJarsDbContext db, Book book, CancellationToken cancellationToken)
    {
        var id = book.Id;
        return new BookDeletionPreview(
            book.Name, book.OpeningDate, book.LockDate,
            await db.Transactions.CountAsync(t => t.BookId == id, cancellationToken),
            await db.PlannedExpenses.CountAsync(p => p.BookId == id, cancellationToken),
            await db.RecurringPlannedExpenses.CountAsync(r => r.BookId == id, cancellationToken),
            await db.CategoryBudgets.CountAsync(b => b.BookId == id, cancellationToken),
            await db.BookMembers.CountAsync(m => m.BookId == id, cancellationToken),
            await db.AuditEntries.CountAsync(e => e.BookId == id.Value, cancellationToken));
    }
}
