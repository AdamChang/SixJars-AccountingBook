using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SixJars.Domain.Members;

namespace SixJars.Infrastructure.Persistence;

internal sealed class BookMemberConfiguration : IEntityTypeConfiguration<BookMember>
{
    public void Configure(EntityTypeBuilder<BookMember> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        // RFC 5321 的 email 上限。
        builder.Property(m => m.Email).HasMaxLength(320);
        builder.Property(m => m.GoogleSubject).HasMaxLength(255);
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(32);
        // 白名單：同一本帳本同一個 email 只能出現一次（email 已在 Domain 正規化）。
        builder.HasIndex(m => new { m.BookId, m.Email }).IsUnique();
        // 授權（BookAccessBehavior）與 /api/me 以登入者的 sub 查詢。
        builder.HasIndex(m => m.GoogleSubject);
    }
}
