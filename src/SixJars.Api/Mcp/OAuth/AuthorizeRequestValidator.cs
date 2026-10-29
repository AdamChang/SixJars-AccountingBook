using Microsoft.Extensions.Options;

namespace SixJars.Api.Mcp.OAuth;

/// <summary>白名單 host 等授權伺服器設定（組態 <c>Mcp:AllowedClientIdHosts</c>）。</summary>
internal sealed class McpOAuthOptions
{
    /// <summary>CIMD 的 client_id 只接受這些 host（不分大小寫、須完全相同，不含子網域）；預設只有 Claude。</summary>
    public string[] AllowedClientIdHosts { get; set; } = ["claude.ai"];
}

/// <summary>authorize 請求的參數；GET 取自 query，POST（同意）取自表單。</summary>
internal sealed record AuthorizeParameters(
    string? ResponseType, string? ClientId, string? RedirectUri, string? CodeChallenge, string? CodeChallengeMethod,
    string? State, string? Scope, string? Resource)
{
    public static AuthorizeParameters From(Func<string, string?> read) => new(
        read("response_type"), read("client_id"), read("redirect_uri"), read("code_challenge"), read("code_challenge_method"),
        read("state"), read("scope"), read("resource"));
}

/// <summary>驗證結果：三種之一。</summary>
internal abstract record AuthorizeValidation
{
    /// <summary>client_id 或 redirect_uri 不可信：只能顯示錯誤頁，絕不轉址（RFC 6749 §4.1.2.1）。</summary>
    public sealed record Rejected(string Message) : AuthorizeValidation;

    /// <summary>client 可信、其他參數有誤：帶 error 轉回 client。</summary>
    public sealed record RedirectError(string RedirectUri, string Error, string? State) : AuthorizeValidation;

    public sealed record Valid(string ClientId, string ClientHost, string RedirectUri, string CodeChallenge, string Scope, string Resource, string? State)
        : AuthorizeValidation;
}

/// <summary>
/// authorize 的參數檢查（ADR 0013）。GET 顯示同意頁前與 POST 發 code 前各跑一次：表單的 hidden 欄位可以被竄改，POST 不信任它們。
/// </summary>
internal sealed class AuthorizeRequestValidator(IClientMetadataFetcher fetcher, IOptions<McpOAuthOptions> options)
{
    public async Task<AuthorizeValidation> ValidateAsync(AuthorizeParameters p, HttpRequest request, CancellationToken cancellationToken)
    {
        // 1. client_id：HTTPS URL、host 在白名單。先檢查 host 再對外連線，白名單外的網址不會被抓（SSRF）。
        if (!Uri.TryCreate(p.ClientId, UriKind.Absolute, out var clientUri)
            || clientUri.Scheme != Uri.UriSchemeHttps
            || clientUri.Fragment.Length > 0
            || !options.Value.AllowedClientIdHosts.Contains(clientUri.Host, StringComparer.OrdinalIgnoreCase))
        {
            return new AuthorizeValidation.Rejected("不受信任的應用程式。");
        }

        // 2. 文件是 client 自己宣稱的，client_id 必須等於網址本身，否則是拿別人的文件冒名。
        var metadata = await fetcher.FetchAsync(clientUri, cancellationToken);
        if (metadata is null || metadata.ClientId != p.ClientId)
        {
            return new AuthorizeValidation.Rejected("無法確認應用程式的身分。");
        }

        // 3. redirect_uri 必須登記在文件裡；不符就不能轉址，否則 code 會被送到別處。
        if (p.RedirectUri is null || !RedirectUris.IsRegistered(p.RedirectUri, metadata.RedirectUris))
        {
            return new AuthorizeValidation.Rejected("不受信任的轉址網址。");
        }

        // 以下錯誤都轉回 client。
        if (p.ResponseType != "code")
        {
            return new AuthorizeValidation.RedirectError(p.RedirectUri, "unsupported_response_type", p.State);
        }

        // Claude 每次都送 S256（OAuth 2.1 對 public client 的要求）；plain 等於沒有 PKCE。
        if (string.IsNullOrEmpty(p.CodeChallenge) || p.CodeChallengeMethod != "S256")
        {
            return new AuthorizeValidation.RedirectError(p.RedirectUri, "invalid_request", p.State);
        }

        // 只有唯讀一種 scope；省略時就是它。
        var scopes = (p.Scope ?? McpOAuth.Scope).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (scopes.Length == 0 || scopes.Any(s => s != McpOAuth.Scope))
        {
            return new AuthorizeValidation.RedirectError(p.RedirectUri, "invalid_scope", p.State);
        }

        // RFC 8707：token 只能給本服務的 /mcp 用。
        var resource = McpOAuth.Resource(request);
        if (p.Resource is not null && p.Resource != resource)
        {
            return new AuthorizeValidation.RedirectError(p.RedirectUri, "invalid_target", p.State);
        }

        return new AuthorizeValidation.Valid(p.ClientId!, clientUri.Host, p.RedirectUri, p.CodeChallenge, McpOAuth.Scope, resource, p.State);
    }
}
