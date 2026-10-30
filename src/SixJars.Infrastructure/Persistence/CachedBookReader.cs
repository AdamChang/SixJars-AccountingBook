using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SixJars.Application.Books;
using SixJars.Domain.Books;
using SixJars.Domain.Common;

namespace SixJars.Infrastructure.Persistence;

/// <summary>
/// 以 Books 那一列的 xmin 當版本的 in-memory 快取。Cloud Run 有多個 instance、CLI 又是另一個 process，
/// 所以不靠「寫入時清掉快取」，而是每次先用 1 次往返讀版本：版本相同就用快取，不同才重新載入（5 次往返，SplitQuery）。
/// 設定（owned collection）變動時 <see cref="SixJarsDbContext"/> 會強制更新 owner 列，xmin 因此跟著變。
/// </summary>
public sealed class CachedBookReader(SixJarsDbContext db, IMemoryCache cache) : IBookReader
{
    // 帳本刪除後不會再有人查，靠滑動到期回收記憶體；版本檢查已保證正確性，到期長短只影響記憶體。
    private static readonly MemoryCacheEntryOptions EntryOptions = new() { SlidingExpiration = TimeSpan.FromHours(1) };

    public async Task<Book?> FindAsync(BookId bookId, CancellationToken cancellationToken)
    {
        var key = CacheKey(bookId);
        if (cache.TryGetValue(key, out CachedBook? cached))
        {
            var version = await db.Books.Where(b => b.Id == bookId)
                .Select(b => (uint?)EF.Property<uint>(b, "xmin"))
                .SingleOrDefaultAsync(cancellationToken);
            if (version == cached!.Version)
            {
                return cached.Book;
            }

            if (version is null)
            {
                cache.Remove(key);
                return null;
            }
        }

        // 版本與帳本在同一次 split query 讀出：版本來自第一條（Books），集合來自之後的查詢，
        // 集合只可能比版本新，不會比版本舊；最壞情況是下次多重新載入一次，不會留下過期的資料。
        var loaded = await db.Books.AsNoTracking().Where(b => b.Id == bookId)
            .Select(b => new CachedBook(b, EF.Property<uint>(b, "xmin")))
            .SingleOrDefaultAsync(cancellationToken);
        if (loaded is null)
        {
            cache.Remove(key);
            return null;
        }

        cache.Set(key, loaded, EntryOptions);
        return loaded.Book;
    }

    private static string CacheKey(BookId bookId) => $"book:{bookId.Value}";

    private sealed record CachedBook(Book Book, uint Version);
}
