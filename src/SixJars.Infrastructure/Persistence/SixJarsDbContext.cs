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

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        Database.BeginTransactionAsync(cancellationToken);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<BookId>().HaveConversion<BookIdConverter>();
        configurationBuilder.Properties<AccountId>().HaveConversion<AccountIdConverter>();
        configurationBuilder.Properties<PlanningFundId>().HaveConversion<PlanningFundIdConverter>();
        configurationBuilder.Properties<CategoryId>().HaveConversion<CategoryIdConverter>();
        configurationBuilder.Properties<TransactionId>().HaveConversion<TransactionIdConverter>();
        configurationBuilder.Properties<PlannedExpenseId>().HaveConversion<PlannedExpenseIdConverter>();
        configurationBuilder.Properties<RecurringPlannedExpenseId>().HaveConversion<RecurringPlannedExpenseIdConverter>();
        configurationBuilder.Properties<BudgetMonth>().HaveConversion<BudgetMonthConverter>();
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SixJarsDbContext).Assembly);
}
