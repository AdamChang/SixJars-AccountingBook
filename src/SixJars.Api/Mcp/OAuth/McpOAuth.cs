namespace SixJars.Api.Mcp.OAuth;

/// <summary>內建 OAuth 授權伺服器的固定值與網址（ADR 0013）。</summary>
internal static class McpOAuth
{
    /// <summary>唯一的 scope：唯讀。MCP server 也只註冊查詢 tool，兩邊同時保證唯讀。</summary>
    public const string Scope = "ledger:read";

    public const string ScopeClaim = "scope";

    public static readonly TimeSpan AuthorizationCodeLifetime = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    /// <summary>
    /// 授權伺服器的 issuer，同時是 Api 對外的根網址：取自請求本身。Cloud Run 由 <c>UseForwardedHeaders</c> 還原成 https，
    /// 測試是 https://localhost。不另設組態，本機、測試與正式環境就不會因為網址設定不一致而讓 token audience 對不上。
    /// </summary>
    public static string Issuer(HttpRequest request) => $"{request.Scheme}://{request.Host}{request.PathBase}";

    /// <summary>MCP endpoint 的網址，也是 access token 的 audience（RFC 8707 的 resource）。</summary>
    public static string Resource(HttpRequest request) => $"{Issuer(request)}{McpSetup.Path}";
}
