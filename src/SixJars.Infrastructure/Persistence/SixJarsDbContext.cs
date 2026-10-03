using Microsoft.EntityFrameworkCore;
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

    public uint GetVersion(object entity) => (uint)Entry(entity).Property("xmin").CurrentValue!;

    // xmin 在 T23 才加入 configuration，屆時以測試驅動完成。
    public void ExpectVersion(object entity, uint version) => throw new NotImplementedException();

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
