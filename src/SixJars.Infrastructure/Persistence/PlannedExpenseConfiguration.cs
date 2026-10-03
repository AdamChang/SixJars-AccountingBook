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
        builder.HasIndex(p => new { p.BookId, p.BudgetMonth });
    }
}
