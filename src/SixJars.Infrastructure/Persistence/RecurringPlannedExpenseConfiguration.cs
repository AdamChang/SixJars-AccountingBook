using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SixJars.Domain.Planning;

namespace SixJars.Infrastructure.Persistence;

internal sealed class RecurringPlannedExpenseConfiguration : IEntityTypeConfiguration<RecurringPlannedExpense>
{
    public void Configure(EntityTypeBuilder<RecurringPlannedExpense> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        // 樂觀並行版本，同 PlannedExpenseConfiguration（P4 K plan D9）。
        builder.Property<uint>("xmin").IsRowVersion();
        builder.Property(r => r.Frequency).HasConversion<string>().HasMaxLength(16);
        // 週期月份存成 integer[]；公開的 Months 是唯讀檢視，對應到私有欄位（P4 K plan D10）。
        builder.Ignore(r => r.Months);
        builder.Property<int[]>("_months").HasColumnName("Months");
        builder.Property(r => r.Note).HasMaxLength(500);
        builder.HasIndex(r => r.BookId);
    }
}
