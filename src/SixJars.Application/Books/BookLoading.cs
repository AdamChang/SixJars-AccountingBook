using Microsoft.EntityFrameworkCore;
using SixJars.Application.Common;
using SixJars.Domain.Books;
using SixJars.Domain.Common;

namespace SixJars.Application.Books;

internal static class BookLoading
{
    /// <summary>載入要修改的帳本（追蹤變更，不經快取）；找不到時擲 <see cref="NotFoundException"/>。</summary>
    public static async Task<Book> GetBookForUpdateAsync(this ISixJarsDbContext db, Guid bookId, CancellationToken cancellationToken)
    {
        var id = new BookId(bookId);
        return await db.Books.SingleOrDefaultAsync(b => b.Id == id, cancellationToken)
            ?? throw new NotFoundException($"找不到帳本 {bookId}。");
    }

    /// <summary>
    /// 載入只讀的帳本（經 <see cref="IBookReader"/> 快取），例如建立交易時只拿來驗證帳戶與分類；找不到時擲 <see cref="NotFoundException"/>。
    /// 回傳的實例可能與其他 request 共用，不可修改。
    /// </summary>
    public static async Task<Book> GetReadOnlyBookAsync(this IBookReader books, Guid bookId, CancellationToken cancellationToken) =>
        await books.FindAsync(new BookId(bookId), cancellationToken)
            ?? throw new NotFoundException($"找不到帳本 {bookId}。");
}
