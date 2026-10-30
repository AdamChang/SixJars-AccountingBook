using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SixJars.Application.Auditing;
using SixJars.Application.Common;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Members;
using SixJars.Domain.Planning;
using SixJars.Domain.Transactions;

namespace SixJars.Infrastructure.Persistence;

/// <summary>帳本的 EF Core 持久層（spec D1-b）：交易連同分錄實體化存檔。</summary>
public sealed class SixJarsDbContext(DbContextOptions<SixJarsDbContext> options)
    : DbContext(options), ISixJarsDbContext, IDataProtectionKeyContext
{
    public DbSet<Book> Books => Set<Book>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<PlannedExpense> PlannedExpenses => Set<PlannedExpense>();
    public DbSet<RecurringPlannedExpense> RecurringPlannedExpenses => Set<RecurringPlannedExpense>();
    public DbSet<CategoryBudget> CategoryBudgets => Set<CategoryBudget>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<BookMember> BookMembers => Set<BookMember>();
    public DbSet<BookCreator> BookCreators => Set<BookCreator>();

    /// <summary>cookie 加密用的 Data Protection key；存在 DB，Cloud Run 換 instance 時登入狀態才不會失效（ADR 0005）。</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public uint GetVersion(object entity) => (uint)Entry(entity).Property("xmin").CurrentValue!;

    public void ExpectVersion(object entity, uint version)
    {
        var entry = Entry(entity);
        entry.Property("xmin").OriginalValue = version;

        // 只改 owned 分錄時，EF 不會更新 owner，也就不會檢查 xmin（spike S2b）；強制整筆 UPDATE，WHERE 才會帶上版本（S2c）。
        entry.State = EntityState.Modified;
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        TouchBooksWithChangedSettings();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        TouchBooksWithChangedSettings();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// 只改帳本的 owned 設定（帳戶、分類…）時 EF 不會更新 Books 那一列（spike S2b），xmin 也就不變；
    /// 強制整筆 UPDATE，CachedBookReader 的版本檢查才看得到這次異動（同 <see cref="ExpectVersion"/> 的 S2c 手法）。
    /// </summary>
    private void TouchBooksWithChangedSettings()
    {
        var changedBookIds = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && e.Metadata.IsOwned()
                && e.Metadata.FindOwnership()!.PrincipalEntityType.ClrType == typeof(Book))
            .Select(e => (BookId)e.Property("BookId").CurrentValue!)
            .ToHashSet();
        if (changedBookIds.Count == 0)
        {
            return;
        }

        foreach (var book in ChangeTracker.Entries<Book>().Where(b => b.State == EntityState.Unchanged && changedBookIds.Contains(b.Entity.Id)))
        {
            book.State = EntityState.Modified;
        }
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        Database.BeginTransactionAsync(cancellationToken);

    public Task AcquireTransactionLockAsync(string key, CancellationToken cancellationToken) =>
        // hashtextextended 把任意字串轉成 bigint；比 hashtext 的 32 bit 不容易碰撞。
        Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<BookId>().HaveConversion<BookIdConverter>();
        configurationBuilder.Properties<AccountId>().HaveConversion<AccountIdConverter>();
        configurationBuilder.Properties<PlanningFundId>().HaveConversion<PlanningFundIdConverter>();
        configurationBuilder.Properties<CategoryId>().HaveConversion<CategoryIdConverter>();
        configurationBuilder.Properties<TransactionId>().HaveConversion<TransactionIdConverter>();
        configurationBuilder.Properties<PlannedExpenseId>().HaveConversion<PlannedExpenseIdConverter>();
        configurationBuilder.Properties<RecurringPlannedExpenseId>().HaveConversion<RecurringPlannedExpenseIdConverter>();
        configurationBuilder.Properties<TagId>().HaveConversion<TagIdConverter>();
        configurationBuilder.Properties<BudgetMonth>().HaveConversion<BudgetMonthConverter>();
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SixJarsDbContext).Assembly);
}
