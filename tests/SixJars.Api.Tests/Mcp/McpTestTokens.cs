using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Api.Mcp.OAuth;
using SixJars.Domain.Members;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;

namespace SixJars.Api.Tests.Mcp;

/// <summary>
/// 不走 OAuth 流程，直接在 DB 建立授權並發出 access token：讓 /mcp 的保護與 tool 的測試不依賴 authorize／token 端點。
/// 完整流程由 <c>McpEndToEndTests</c> 涵蓋。
/// </summary>
internal static class McpTestTokens
{
    public const string Resource = "https://localhost/mcp";

    public static async Task<(McpGrant Grant, string AccessToken)> IssueAsync(
        ApiFactory factory, CancellationToken cancellationToken, string subject = ApiFactory.DefaultSubject, string audience = Resource)
    {
        var tokens = factory.Services.GetRequiredService<McpTokens>();
        var now = factory.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        var grant = McpGrant.Issue(subject, TestAuthHandler.EmailOf(subject), "https://claude.ai/oauth/mcp-oauth-client-metadata",
            McpOAuth.Scope, Guid.NewGuid(), McpTokens.Hash(McpTokens.NewRefreshToken()), now, McpOAuth.RefreshTokenLifetime);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
            db.McpGrants.Add(grant);
            await db.SaveChangesAsync(cancellationToken);
        }

        return (grant, tokens.CreateAccessToken(grant, audience, now));
    }

    /// <summary>不經 cookie、不帶 Test header 的 client：只靠 bearer token 認證。</summary>
    public static HttpClient CreateBearerClient(ApiFactory factory, string? accessToken)
    {
        var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        if (accessToken is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return client;
    }

    public static async Task RevokeAsync(ApiFactory factory, Guid grantId, CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
        var grant = await db.McpGrants.FindAsync([grantId], cancellationToken);
        grant!.Revoke(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>MCP initialize 的 JSON-RPC 請求（Streamable HTTP 要求 Accept 同時列出 JSON 與 SSE）。</summary>
    public static HttpRequestMessage InitializeRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}""",
                System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        return request;
    }
}
