using Microsoft.EntityFrameworkCore;
using SixJars.Domain.Books;
using SixJars.Domain.Planning;
using SixJars.Domain.Transactions;

namespace SixJars.Application.Common;

/// <summary>Application 直接以 EF Core 讀寫；provider 特有的細節（xmin）藏在這裡的方法後面。</summary>
public interface ISixJarsDbContext
{
    DbSet<Book> Books { get; }
    DbSet<Transaction> Transactions { get; }
    DbSet<PlannedExpense> PlannedExpenses { get; }

    /// <summary>樂觀並行版本（PostgreSQL xmin）；回應給前端，修改時帶回來。</summary>
    uint GetVersion(object entity);

    /// <summary>以前端帶回的版本做並行檢查，並強制整筆標為 Modified（只改 owned 分錄時 EF 不會檢查版本，見 spike S2b）。</summary>
    void ExpectVersion(object entity, uint version);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
