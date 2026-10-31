using FluentAssertions;
using SixJars.Domain.Members;
using Xunit;

namespace SixJars.Domain.Tests.Members;

/// <summary>MCP 授權（ADR 0013）：refresh token 每次輪替，舊 token 再出現視為外洩而撤銷整個授權。</summary>
public class McpGrantTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    private static McpGrant Issue() =>
        McpGrant.Issue("sub-1", "adam@example.com", "https://claude.ai/oauth/mcp-oauth-client-metadata", "ledger:read",
            Guid.NewGuid(), "hash-1", Now, Lifetime);

    [Fact]
    public void Issue_starts_active_with_refresh_token_expiry()
    {
        var grant = Issue();

        grant.IsActive.Should().BeTrue();
        grant.RefreshTokenHash.Should().Be("hash-1");
        grant.PreviousRefreshTokenHash.Should().BeNull();
        grant.RefreshTokenExpiresAt.Should().Be(Now + Lifetime);
        grant.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void Refresh_with_current_hash_rotates()
    {
        var grant = Issue();
        var later = Now.AddDays(1);

        var outcome = grant.Refresh("hash-1", "hash-2", later, Lifetime);

        outcome.Should().Be(McpRefreshOutcome.Rotated);
        grant.RefreshTokenHash.Should().Be("hash-2");
        grant.PreviousRefreshTokenHash.Should().Be("hash-1");
        grant.RefreshTokenExpiresAt.Should().Be(later + Lifetime);
        grant.LastRefreshedAt.Should().Be(later);
        grant.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Refresh_with_previous_hash_revokes_grant()
    {
        var grant = Issue();
        grant.Refresh("hash-1", "hash-2", Now, Lifetime);
        var later = Now.AddMinutes(5);

        var outcome = grant.Refresh("hash-1", "hash-3", later, Lifetime);

        outcome.Should().Be(McpRefreshOutcome.Reused);
        grant.IsActive.Should().BeFalse();
        grant.RevokedAt.Should().Be(later);
        grant.RefreshTokenHash.Should().Be("hash-2", "重放時不能輪替出新的 token");
    }

    [Fact]
    public void Refresh_with_unknown_hash_is_invalid_and_keeps_grant()
    {
        var grant = Issue();

        var outcome = grant.Refresh("guess", "hash-2", Now, Lifetime);

        outcome.Should().Be(McpRefreshOutcome.Invalid);
        grant.IsActive.Should().BeTrue();
        grant.RefreshTokenHash.Should().Be("hash-1");
    }

    [Fact]
    public void Refresh_after_expiry_is_invalid()
    {
        var grant = Issue();

        var outcome = grant.Refresh("hash-1", "hash-2", Now + Lifetime, Lifetime);

        outcome.Should().Be(McpRefreshOutcome.Invalid);
        grant.RefreshTokenHash.Should().Be("hash-1");
    }

    [Fact]
    public void Refresh_on_revoked_grant_is_invalid()
    {
        var grant = Issue();
        grant.Revoke(Now);

        var outcome = grant.Refresh("hash-1", "hash-2", Now.AddMinutes(1), Lifetime);

        outcome.Should().Be(McpRefreshOutcome.Invalid);
        grant.RefreshTokenHash.Should().Be("hash-1");
    }

    [Fact]
    public void Revoke_is_idempotent()
    {
        var grant = Issue();
        grant.Revoke(Now);

        grant.Revoke(Now.AddDays(1));

        grant.RevokedAt.Should().Be(Now);
    }
}
