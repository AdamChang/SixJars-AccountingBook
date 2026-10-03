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

        builder.Navigation(t => t.Postings).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
