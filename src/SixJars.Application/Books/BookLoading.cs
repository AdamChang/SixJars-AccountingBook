using Microsoft.EntityFrameworkCore;
using SixJars.Application.Common;
using SixJars.Domain.Books;
using SixJars.Domain.Common;

namespace SixJars.Application.Books;

internal static class BookLoading
{
    /// <summary>載入要修改的帳本（追蹤變更）；找不到時擲 <see cref="NotFoundException"/>。</summary>
    public static async Task<Book> GetBookForUpdateAsync(this ISixJarsDbContext db, Guid bookId, CancellationToken cancellationToken)
    {
        var id = new BookId(bookId);
        return await db.Books.SingleOrDefaultAsync(b => b.Id == id, cancellationToken)
            ?? throw new NotFoundException($"找不到帳本 {bookId}。");
    }

    /// <summary>載入只讀的帳本（不追蹤變更），例如建立交易時只拿來驗證帳戶與分類；找不到時擲 <see cref="NotFoundException"/>。</summary>
    public static async Task<Book> GetBookAsNoTrackingAsync(this ISixJarsDbContext db, Guid bookId, CancellationToken cancellationToken)
    {
        var id = new BookId(bookId);
        return await db.Books.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken)
            ?? throw new NotFoundException($"找不到帳本 {bookId}。");
    }
}
