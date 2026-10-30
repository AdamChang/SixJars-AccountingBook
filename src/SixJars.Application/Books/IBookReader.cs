using SixJars.Domain.Books;
using SixJars.Domain.Common;

namespace SixJars.Application.Books;

/// <summary>
/// 唯讀帳本（含帳戶、財務規劃帳戶、分類、標籤）的來源。實作會快取，同一本帳本可能回傳同一個實例給多個 request，
/// 所以<b>拿到的 <see cref="Book"/> 絕對不能修改</b>；要修改請用 <see cref="BookLoading.GetBookForUpdateAsync"/>。
/// </summary>
public interface IBookReader
{
    /// <summary>找不到（或已刪除）時回傳 null。</summary>
    Task<Book?> FindAsync(BookId bookId, CancellationToken cancellationToken);
}
