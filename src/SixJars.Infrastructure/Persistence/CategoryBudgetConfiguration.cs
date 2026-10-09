using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SixJars.Domain.Planning;

namespace SixJars.Infrastructure.Persistence;

internal sealed class CategoryBudgetConfiguration : IEntityTypeConfiguration<CategoryBudget>
{
    public void Configure(EntityTypeBuilder<CategoryBudget> builder)
    {
        // 主鍵就是分類 Id（P4 L plan Q6）。分類是 Book 的 owned entity，不能設 FK 指過去（D2），參照完整性由 SettingReferences 擋。
        builder.HasKey(b => b.CategoryId);
        builder.Property(b => b.CategoryId).ValueGeneratedNever();
        builder.HasIndex(b => b.BookId);
        // 不做樂觀並行（P4 L plan Q3），所以沒有 xmin。

        // 覆寫值一個月份一列（D1）：兩台裝置同時改不同月份不會互相覆蓋。
        builder.OwnsMany(b => b.Overrides, monthOverride =>
        {
            monthOverride.ToTable("CategoryBudgetOverrides");
            monthOverride.WithOwner().HasForeignKey("CategoryId");
            monthOverride.HasKey("CategoryId", nameof(BudgetOverride.Month));
            monthOverride.Property(o => o.Month).HasColumnName("BudgetMonth");
        });
        builder.Navigation(b => b.Overrides).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
