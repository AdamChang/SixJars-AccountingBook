using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SixJars.Domain.Planning;

namespace SixJars.Infrastructure.Persistence;

internal sealed class PlannedExpenseConfiguration : IEntityTypeConfiguration<PlannedExpense>
{
    public void Configure(EntityTypeBuilder<PlannedExpense> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        // 樂觀並行版本：PostgreSQL 的系統欄位 xmin（spike S2），不會真的建立欄位；版本的讀寫見 SixJarsDbContext。
        builder.Property<uint>("xmin").IsRowVersion();
        // 軟刪除（ADR 0006）：已刪除的預定支出預設查不到，包含 LedgerSnapshotLoader 與 SQL 彙總；要看全部時用 IgnoreQueryFilters()。
        builder.HasQueryFilter(p => p.DeletedAt == null);
        builder.HasIndex(p => new { p.BookId, p.BudgetMonth });
        // ADR 0009：同一來源同一月只產生一次，「包含已刪除」，所以條件只有 SourceId 不為 null。
        builder.HasIndex(p => new { p.SourceId, p.BudgetMonth }).IsUnique().HasFilter("\"SourceId\" IS NOT NULL");
        // 被參照的週期項目不能刪除（Domain 先擋，FK 是最後一道防線）。
        builder.HasOne<RecurringPlannedExpense>().WithMany().HasForeignKey(p => p.SourceId).OnDelete(DeleteBehavior.Restrict);
    }
}
