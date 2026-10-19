using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SixJars.Domain.Members;

namespace SixJars.Infrastructure.Persistence;

internal sealed class BookCreatorConfiguration : IEntityTypeConfiguration<BookCreator>
{
    public void Configure(EntityTypeBuilder<BookCreator> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        // 與 BookMemberConfiguration 一致：RFC 5321 的 email 上限、sub 255。
        builder.Property(c => c.Email).HasMaxLength(320);
        builder.Property(c => c.GoogleSubject).HasMaxLength(255);
        // 同一個 email 只出現一次（email 已在 Domain 正規化）。
        builder.HasIndex(c => c.Email).IsUnique();
        // 登入與 /api/me 以登入者的 sub 查詢。
        builder.HasIndex(c => c.GoogleSubject);
    }
}
