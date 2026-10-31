using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SixJars.Application.Common;

namespace SixJars.Api.Mcp.OAuth;

/// <summary>
/// /mcp 的 authenticate：驗證 access token 未過期、audience 是本服務的 /mcp，而且授權沒有被撤銷。
/// challenge 轉給 SDK 的 MCP scheme，由它在 401 加上 <c>resource_metadata</c>（見 <see cref="McpSetup"/>）。
/// </summary>
/// <remarks>
/// 每次請求以主鍵查一次授權，換到撤銷立即生效（ADR 0013）。產生的 claim 名稱與 Google cookie 相同（sub、email），
/// 所以 <c>HttpCurrentUser</c> 與 <c>BookAccessBehavior</c> 不用改。
/// </remarks>
internal sealed class McpBearerHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, McpTokens tokens, ISixJarsDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "McpBearer";
    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? authorization = Request.Headers.Authorization;
        if (authorization is null || !authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = tokens.ReadAccessToken(authorization[BearerPrefix.Length..].Trim());
        if (token is null || token.Audience != McpOAuth.Resource(Request))
        {
            return AuthenticateResult.Fail("invalid_token");
        }

        var active = await db.McpGrants.AsNoTracking().AnyAsync(g => g.Id == token.GrantId && g.RevokedAt == null, Context.RequestAborted);
        if (!active)
        {
            return AuthenticateResult.Fail("invalid_token");
        }

        List<Claim> claims = [new("sub", token.Subject), new(McpOAuth.ScopeClaim, token.Scope)];
        if (token.Email is not null)
        {
            claims.Add(new Claim("email", token.Email));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
