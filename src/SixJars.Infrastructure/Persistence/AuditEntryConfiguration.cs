using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SixJars.Application.Auditing;

namespace SixJars.Infrastructure.Persistence;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.ActorSubject).HasMaxLength(255);
        builder.Property(e => e.Action).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.EntityType).HasMaxLength(64);
        // 快照存成 jsonb（spike S5），日後可以直接在 SQL 裡查欄位。
        builder.Property(e => e.Before).HasColumnType("jsonb");
        builder.Property(e => e.After).HasColumnType("jsonb");
        // 修改歷史以「帳本 + 實體」查詢（GET /audit?entityId=）。
        builder.HasIndex(e => new { e.BookId, e.EntityId });
    }
}
