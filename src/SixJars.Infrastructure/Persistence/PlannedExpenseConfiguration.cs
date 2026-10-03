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
        builder.HasIndex(p => new { p.BookId, p.BudgetMonth });
    }
}
