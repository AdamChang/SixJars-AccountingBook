using Microsoft.EntityFrameworkCore;
using SixJars.Application.Auditing;
using SixJars.Application.Common;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using SixJars.Domain.Transactions;

namespace SixJars.Infrastructure.Persistence;

/// <summary>帳本的 EF Core 持久層（spec D1-b）：交易連同分錄實體化存檔。</summary>
public sealed class SixJarsDbContext(DbContextOptions<SixJarsDbContext> options) : DbContext(options), ISixJarsDbContext
{
    public DbSet<Book> Books => Set<Book>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<PlannedExpense> PlannedExpenses => Set<PlannedExpense>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public uint GetVersion(object entity) => (uint)Entry(entity).Property("xmin").CurrentValue!;

    public void ExpectVersion(object entity, uint version)
    {
        var entry = Entry(entity);
        entry.Property("xmin").OriginalValue = version;

        // 只改 owned 分錄時，EF 不會更新 owner，也就不會檢查 xmin（spike S2b）；強制整筆 UPDATE，WHERE 才會帶上版本（S2c）。
        entry.State = EntityState.Modified;
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<BookId>().HaveConversion<BookIdConverter>();
        configurationBuilder.Properties<AccountId>().HaveConversion<AccountIdConverter>();
        configurationBuilder.Properties<PlanningFundId>().HaveConversion<PlanningFundIdConverter>();
        configurationBuilder.Properties<CategoryId>().HaveConversion<CategoryIdConverter>();
        configurationBuilder.Properties<TransactionId>().HaveConversion<TransactionIdConverter>();
        configurationBuilder.Properties<PlannedExpenseId>().HaveConversion<PlannedExpenseIdConverter>();
        configurationBuilder.Properties<BudgetMonth>().HaveConversion<BudgetMonthConverter>();
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SixJarsDbContext).Assembly);
}
