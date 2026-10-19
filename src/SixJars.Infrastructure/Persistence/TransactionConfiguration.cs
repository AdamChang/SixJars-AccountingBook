using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SixJars.Domain.Transactions;

namespace SixJars.Infrastructure.Persistence;

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        // 樂觀並行版本：PostgreSQL 的系統欄位 xmin（spike S2），不會真的建立欄位；版本的讀寫見 SixJarsDbContext。
        builder.Property<uint>("xmin").IsRowVersion();
        // 軟刪除（ADR 0006）：已刪除的交易預設查不到，包含 LedgerSnapshotLoader 與 SQL 彙總；要看全部時用 IgnoreQueryFilters()。
        builder.HasQueryFilter(t => t.DeletedAt == null);
        builder.Property(t => t.Kind).HasConversion<string>();
        builder.Property(t => t.Note).HasMaxLength(500);
        builder.HasIndex(t => new { t.BookId, t.BudgetMonth });

        builder.OwnsMany(t => t.Postings, posting =>
        {
            posting.ToTable("Postings");
            posting.WithOwner().HasForeignKey("TransactionId");
            posting.Property<int>("Id");
            posting.HasKey("Id");
            posting.HasIndex(p => p.AccountId);
        });

        // TagId 不建指向 Tags 的 FK：EF 不允許指向 owned type 的關聯，參照完整性由 Domain 保證（spec D1）。
        builder.OwnsMany(t => t.Tags, tag =>
        {
            tag.ToTable("TransactionTags");
            tag.WithOwner().HasForeignKey("TransactionId");
            tag.HasKey("TransactionId", nameof(TransactionTag.TagId));
            tag.HasIndex(x => x.TagId);
        });

        builder.Navigation(t => t.Postings).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(t => t.Tags).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(t => t.TagIds);
    }
}
