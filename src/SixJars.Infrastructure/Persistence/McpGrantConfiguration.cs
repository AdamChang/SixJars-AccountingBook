using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SixJars.Domain.Members;

namespace SixJars.Infrastructure.Persistence;

internal sealed class McpGrantConfiguration : IEntityTypeConfiguration<McpGrant>
{
    public void Configure(EntityTypeBuilder<McpGrant> builder)
    {
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).ValueGeneratedNever();
        // 與 BookMemberConfiguration 一致：sub 255、email 320；client_id 是 URL，上限比照瀏覽器常見的 2048。
        builder.Property(g => g.Subject).HasMaxLength(255);
        builder.Property(g => g.Email).HasMaxLength(320);
        builder.Property(g => g.ClientId).HasMaxLength(2048);
        builder.Property(g => g.Scope).HasMaxLength(100);
        // SHA-256 的 hex。
        builder.Property(g => g.RefreshTokenHash).HasMaxLength(64);
        builder.Property(g => g.PreviousRefreshTokenHash).HasMaxLength(64);
        builder.Ignore(g => g.IsActive);
        // 樂觀並行（同 PlannedExpenseConfiguration 的 xmin）：同一個 refresh token 同時被使用時，只有先寫入的輪替成功；
        // 少了它兩邊都會 Rotated、後寫的覆蓋先寫的，重放偵測在競態下失效。
        builder.Property<uint>("xmin").IsRowVersion();
        // authorization code 只能兌換一次：第二次兌換插入時撞到唯一性（ADR 0013）。
        builder.HasIndex(g => g.AuthorizationCodeId).IsUnique();
        // refresh 以 hash 查詢；上一個 hash 用來偵測重放。
        builder.HasIndex(g => g.RefreshTokenHash);
        builder.HasIndex(g => g.PreviousRefreshTokenHash);
        // CLI 撤銷以 sub 查詢。
        builder.HasIndex(g => g.Subject);
    }
}
