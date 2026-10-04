using Microsoft.EntityFrameworkCore;
using SixJars.Domain.Common;
using SixJars.Domain.Ledger;

namespace SixJars.Infrastructure.Persistence;

/// <summary>持久層與計算器之間的接縫：只載入指定帳本的資料（Q6 帳本邊界）。</summary>
public sealed class LedgerSnapshotLoader(SixJarsDbContext db)
{
    public async Task<LedgerSnapshot> LoadAsync(BookId bookId, CancellationToken cancellationToken)
    {
        var book = await db.Books.AsNoTracking().SingleAsync(b => b.Id == bookId, cancellationToken);
        // 計算器依序處理交易，所以固定順序：日期、再依 Id（CreateVersion7，即建立順序）。
        var transactions = await db.Transactions.AsNoTracking().Where(t => t.BookId == bookId)
            .OrderBy(t => t.Date).ThenBy(t => t.Id)
            .ToListAsync(cancellationToken);
        var planned = await db.PlannedExpenses.AsNoTracking().Where(p => p.BookId == bookId).ToListAsync(cancellationToken);
        return new LedgerSnapshot(book, transactions, planned);
    }
}
