using Microsoft.Net.Http.Headers;
using Xunit;

namespace SixJars.Tests.Shared;

/// <summary>
/// <c>GET /api/antiforgery/token</c> 發出的 cookie：<c>XSRF-TOKEN</c>（前端讀取後放進 header 的 request token）
/// 與 antiforgery 自己的 HttpOnly cookie token。兩者都與發出時的登入身分綁定。
/// </summary>
public sealed class ApiXsrfTokens(IReadOnlyList<SetCookieHeaderValue> cookies)
{
    public const string RequestTokenCookie = "XSRF-TOKEN";
    public const string HeaderName = "X-XSRF-TOKEN";

    /// <summary>response 的 <c>Set-Cookie</c>，原樣保留屬性，供測試斷言 HttpOnly、Secure、SameSite。</summary>
    public IReadOnlyList<SetCookieHeaderValue> Cookies => cookies;

    public string RequestToken => cookies.Single(c => c.Name == RequestTokenCookie).Value.ToString();

    /// <summary>以 <paramref name="client"/> 的登入身分取得 token。</summary>
    public static async Task<ApiXsrfTokens> FetchAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/antiforgery/token", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return new ApiXsrfTokens([.. SetCookieHeaderValue.ParseList(response.Headers.GetValues(HeaderNames.SetCookie).ToList())]);
    }

    /// <summary>照瀏覽器與 Angular <c>HttpClient</c> 的行為：送回所有 cookie，並把 <c>XSRF-TOKEN</c> 的值放進 header。</summary>
    public void ApplyTo(HttpClient client)
    {
        client.DefaultRequestHeaders.Add(HeaderNames.Cookie, string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}")));
        client.DefaultRequestHeaders.Add(HeaderName, RequestToken);
    }
}
