using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SixJars.Domain.Books;

namespace SixJars.Infrastructure.Persistence;

internal sealed class BookConfiguration : IEntityTypeConfiguration<Book>
{
    private const int NameMaxLength = 100;

    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.Name).HasMaxLength(NameMaxLength);

        builder.OwnsMany(b => b.Accounts, account =>
        {
            account.ToTable("Accounts");
            account.WithOwner().HasForeignKey("BookId");
            account.HasKey(a => a.Id);
            account.Property(a => a.Id).ValueGeneratedNever();
            account.Property(a => a.Name).HasMaxLength(NameMaxLength);
            account.Property(a => a.Type).HasConversion<string>();
        });

        builder.OwnsMany(b => b.PlanningFunds, fund =>
        {
            fund.ToTable("PlanningFunds");
            fund.WithOwner().HasForeignKey("BookId");
            fund.HasKey(f => f.Id);
            fund.Property(f => f.Id).ValueGeneratedNever();
            fund.Property(f => f.Name).HasMaxLength(NameMaxLength);
        });

        builder.OwnsMany(b => b.Categories, category =>
        {
            category.ToTable("Categories");
            category.WithOwner().HasForeignKey("BookId");
            category.HasKey(c => c.Id);
            category.Property(c => c.Id).ValueGeneratedNever();
            category.Property(c => c.Name).HasMaxLength(NameMaxLength);
            category.Property(c => c.Kind).HasConversion<string>();
            category.Property(c => c.Nature).HasConversion<string>();
        });

        builder.OwnsMany(b => b.Tags, tag =>
        {
            tag.ToTable("Tags");
            tag.WithOwner().HasForeignKey("BookId");
            tag.HasKey(t => t.Id);
            tag.Property(t => t.Id).ValueGeneratedNever();
            // 8 個 text element 可能超過 8 個 UTF-16 字元（spec D8），欄位長度沿用 NameMaxLength。
            tag.Property(t => t.Name).HasMaxLength(NameMaxLength);
            tag.Property(t => t.Color).HasConversion<string>();
        });

        builder.Navigation(b => b.Accounts).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(b => b.PlanningFunds).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(b => b.Categories).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(b => b.Tags).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
