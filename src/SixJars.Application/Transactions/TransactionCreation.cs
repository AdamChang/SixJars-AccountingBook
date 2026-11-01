using SixJars.Domain.Books;
using SixJars.Domain.Transactions;

namespace SixJars.Application.Transactions;

/// <summary>
/// 新增交易的共用步驟（鎖帳日 → 展開分錄 → 標籤檢查）。網頁的 <see cref="CreateTransaction"/> 與 MCP 的
/// <see cref="RecordTransaction"/> 都走這裡，規則只有一份；它只負責建出交易，不加入 DbContext，也不留稽核記錄。
/// </summary>
internal static class TransactionCreation
{
    /// <param name="book">只讀取，不會被修改（可以是 <see cref="Books.IBookReader"/> 的共用實例）。</param>
    public static Transaction Create(Book book, TransactionInput input)
    {
        book.EnsureUnlocked(input.Date);
        var transaction = TransactionBuilder.Build(book, input);
        TransactionBuilder.EnsureNoNewArchivedTags(book, [], transaction.TagIds);
        return transaction;
    }
}
