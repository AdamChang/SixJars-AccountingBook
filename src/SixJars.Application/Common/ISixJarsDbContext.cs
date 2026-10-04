using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SixJars.Application.Auditing;
using SixJars.Domain.Books;
using SixJars.Domain.Members;
using SixJars.Domain.Planning;
using SixJars.Domain.Transactions;

namespace SixJars.Application.Common;

/// <summary>Application 直接以 EF Core 讀寫；provider 特有的細節（xmin）藏在這裡的方法後面。</summary>
public interface ISixJarsDbContext
{
    DbSet<Book> Books { get; }
    DbSet<Transaction> Transactions { get; }
    DbSet<PlannedExpense> PlannedExpenses { get; }

    /// <summary>稽核記錄（append-only）；只由 <see cref="IAuditTrail"/> 加入。</summary>
    DbSet<AuditEntry> AuditEntries { get; }

    /// <summary>帳本成員；白名單即此表（ADR 0005）。</summary>
    DbSet<BookMember> BookMembers { get; }

    /// <summary>樂觀並行版本（PostgreSQL xmin）；回應給前端，修改時帶回來。</summary>
    uint GetVersion(object entity);

    /// <summary>以前端帶回的版本做並行檢查，並強制整筆標為 Modified（只改 owned 分錄時 EF 不會檢查版本，見 spike S2b）。</summary>
    void ExpectVersion(object entity, uint version);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 明確的 DB transaction：跨多次 <see cref="SaveChangesAsync"/> 的寫入（匯入）全部成功或全部不寫入。
    /// 沒有 Commit 就 Dispose 時自動回滾。單次 SaveChanges 本身已是 transaction，一般的 command 不需要。
    /// </summary>
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
}
