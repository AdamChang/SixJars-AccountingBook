using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SixJars.Api.Mcp.OAuth;
using SixJars.Tests.Shared;

namespace SixJars.Api.Tests.Mcp;

/// <summary>
/// 模擬 Claude 走授權流程：PKCE、authorize（GET 同意頁 → POST 同意）、token。不自動轉址、不保存 cookie，每一步都看得到原始回應。
/// </summary>
internal static partial class OAuthTestClient
{
    public const string ClaudeClientId = "https://claude.ai/oauth/mcp-oauth-client-metadata";
    public const string ClaudeRedirectUri = "https://claude.ai/api/mcp/auth_callback";
    public const string Resource = "https://localhost/mcp";

    /// <summary>以假的 CIMD 抓取器建立 factory：不對外連線。</summary>
    public static async Task<(ApiFactory Factory, FakeClientMetadataFetcher Fetcher)> CreateFactoryAsync(
        PostgresFixture postgres, CancellationToken cancellationToken, Action<IServiceCollection>? testServices = null)
    {
        var fetcher = new FakeClientMetadataFetcher();
        var factory = await ApiFactory.CreateAsync(postgres, cancellationToken, services =>
        {
            services.RemoveAll<IClientMetadataFetcher>();
            services.AddSingleton<IClientMetadataFetcher>(fetcher);
            testServices?.Invoke(services);
        });
        return (factory, fetcher);
    }

    /// <summary>不轉址、不保存 cookie；<paramref name="subject"/> 不是 null 時以該身分登入（Test scheme）。</summary>
    public static HttpClient CreateClient(ApiFactory factory, string? subject = ApiFactory.DefaultSubject)
    {
        var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false, AllowAutoRedirect = false });
        if (subject is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.SubjectHeader, subject);
        }

        return client;
    }

    public static (string Verifier, string Challenge) NewPkce()
    {
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return (verifier, WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    public static Dictionary<string, string?> AuthorizeParameters(string challenge, string state = "xyz") => new()
    {
        ["response_type"] = "code",
        ["client_id"] = ClaudeClientId,
        ["redirect_uri"] = ClaudeRedirectUri,
        ["code_challenge"] = challenge,
        ["code_challenge_method"] = "S256",
        ["state"] = state,
        ["scope"] = McpOAuth.Scope,
        ["resource"] = Resource,
    };

    public static string AuthorizeUrl(IDictionary<string, string?> parameters) =>
        QueryHelpers.AddQueryString(OAuthEndpoints.AuthorizePath, parameters);

    /// <summary>GET 同意頁，取出表單的 hidden 欄位與 antiforgery cookie，再以 <paramref name="decision"/> POST 回去。</summary>
    public static async Task<HttpResponseMessage> ConsentAsync(
        HttpClient client, IDictionary<string, string?> parameters, CancellationToken cancellationToken, string decision = "allow")
    {
        var page = await client.GetAsync(AuthorizeUrl(parameters), cancellationToken);
        if (page.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"同意頁回應 {(int)page.StatusCode}");
        }

        var html = await page.Content.ReadAsStringAsync(cancellationToken);
        var fields = HiddenField().Matches(html).ToDictionary(m => WebUtility.HtmlDecode(m.Groups["name"].Value), m => WebUtility.HtmlDecode(m.Groups["value"].Value));
        fields["decision"] = decision;
        using var request = new HttpRequestMessage(HttpMethod.Post, OAuthEndpoints.AuthorizePath) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Add("Cookie", string.Join("; ", page.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0])));
        return await client.SendAsync(request, cancellationToken);
    }

    /// <summary>走完同意流程，回傳 code。</summary>
    public static async Task<string> AuthorizeAsync(HttpClient client, string challenge, CancellationToken cancellationToken)
    {
        var response = await ConsentAsync(client, AuthorizeParameters(challenge), cancellationToken);
        if (response.StatusCode != HttpStatusCode.Redirect)
        {
            throw new InvalidOperationException($"授權回應 {(int)response.StatusCode}");
        }

        return QueryHelpers.ParseQuery(response.Headers.Location!.Query)["code"].ToString();
    }

    public static Task<HttpResponseMessage> TokenAsync(HttpClient client, IDictionary<string, string> form, CancellationToken cancellationToken) =>
        client.PostAsync(OAuthEndpoints.TokenPath, new FormUrlEncodedContent(form), cancellationToken);

    public static Dictionary<string, string> CodeExchange(string code, string verifier) => new()
    {
        ["grant_type"] = "authorization_code",
        ["code"] = code,
        ["code_verifier"] = verifier,
        ["client_id"] = ClaudeClientId,
        ["redirect_uri"] = ClaudeRedirectUri,
        ["resource"] = Resource,
    };

    public static Dictionary<string, string> RefreshRequest(string refreshToken) => new()
    {
        ["grant_type"] = "refresh_token",
        ["refresh_token"] = refreshToken,
        ["client_id"] = ClaudeClientId,
    };

    [GeneratedRegex("""<input type="hidden" name="(?<name>[^"]+)" value="(?<value>[^"]*)">""")]
    private static partial Regex HiddenField();
}

/// <summary>回傳 Claude 的 CIMD 文件，並記錄被呼叫的次數（驗證白名單外的 host 根本不會被抓）。</summary>
internal sealed class FakeClientMetadataFetcher : IClientMetadataFetcher
{
    private int calls;

    public int Calls => calls;

    /// <summary>測試可改成回傳別的文件（例如 client_id 不一致）。</summary>
    public Func<Uri, ClientMetadata?> Respond { get; set; } = clientId => clientId.AbsoluteUri == OAuthTestClient.ClaudeClientId
        ? new ClientMetadata(OAuthTestClient.ClaudeClientId, "Claude", [OAuthTestClient.ClaudeRedirectUri, "http://localhost/callback"])
        : null;

    public Task<ClientMetadata?> FetchAsync(Uri clientId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls);
        return Task.FromResult(Respond(clientId));
    }
}
