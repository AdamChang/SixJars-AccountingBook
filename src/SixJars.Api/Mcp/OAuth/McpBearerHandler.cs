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
/// 所以 <c>HttpCurrentUser</c> 與 <c>BookAccessBehavior</c> 不用改；另外多一個 <c>via</c>（<c>mcp:&lt;client host&gt;</c>），
/// 讓寫入的交易與稽核記錄能標示是經由哪個 client。
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

        var grant = await db.McpGrants.AsNoTracking()
            .Where(g => g.Id == token.GrantId && g.RevokedAt == null)
            .Select(g => new { g.ClientId })
            .FirstOrDefaultAsync(Context.RequestAborted);
        if (grant is null)
        {
            return AuthenticateResult.Fail("invalid_token");
        }

        // via 取自授權記錄的 client_id（發 code 前已驗證在白名單內的 https URL），不取自 token 內容或請求。
        List<Claim> claims = [new("sub", token.Subject), new("via", $"mcp:{new Uri(grant.ClientId).Host}")];

        // 每個 scope 一個 claim：token.Scope 是以空白分隔的字串，policy 的 RequireClaim 是整值比對，
        // 整串放進單一 claim 的話，授予讀寫時 "ledger:read ledger:write" 不等於 "ledger:read"，唯讀 tool 反而全被擋。
        claims.AddRange(token.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(scope => new Claim(McpOAuth.ScopeClaim, scope)));
        if (token.Email is not null)
        {
            claims.Add(new Claim("email", token.Email));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
