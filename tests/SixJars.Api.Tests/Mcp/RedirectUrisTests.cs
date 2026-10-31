using FluentAssertions;
using SixJars.Api.Mcp.OAuth;
using Xunit;

namespace SixJars.Api.Tests.Mcp;

/// <summary>
/// redirect_uri 比對（ADR 0013）：一般網址逐字比對；loopback 忽略 port，因為 Claude Code 每次用不同的臨時 port（RFC 8252 §7.3）。
/// </summary>
public class RedirectUrisTests
{
    private static readonly string[] Registered = ["https://claude.ai/api/mcp/auth_callback", "http://localhost/callback", "http://127.0.0.1/callback"];

    [Theory]
    [InlineData("https://claude.ai/api/mcp/auth_callback")]
    [InlineData("http://localhost:3118/callback")]
    [InlineData("http://localhost/callback")]
    [InlineData("http://127.0.0.1:5000/callback")]
    public void Registered_or_loopback_with_any_port_matches(string requested) =>
        RedirectUris.IsRegistered(requested, Registered).Should().BeTrue();

    [Theory]
    [InlineData("https://claude.ai/api/mcp/auth_callback/")]
    [InlineData("https://claude.ai/api/mcp/auth_callback?x=1")]
    [InlineData("https://claude.ai:8443/api/mcp/auth_callback")]
    [InlineData("https://evil.com/api/mcp/auth_callback")]
    [InlineData("http://evil.com:3118/callback")]
    [InlineData("http://localhost:3118/other")]
    [InlineData("https://localhost:3118/callback")]
    [InlineData("http://localhost.evil.com/callback")]
    [InlineData("not a uri")]
    [InlineData("")]
    public void Anything_else_does_not_match(string requested) =>
        RedirectUris.IsRegistered(requested, Registered).Should().BeFalse();

    [Fact]
    public void Port_is_not_ignored_for_non_loopback_registration() =>
        RedirectUris.IsRegistered("https://claude.ai:443/api/mcp/auth_callback", ["https://claude.ai/api/mcp/auth_callback"])
            .Should().BeFalse("非 loopback 一律逐字比對，不做任何正規化");
}
