using Microsoft.AspNetCore.Antiforgery;

namespace SixJars.Api.Infrastructure;

/// <summary>
/// XSRF 檢查（ADR 0005）：登入靠同源 cookie，別的網站能讓瀏覽器自動帶上它，所以會改變狀態的請求都要附上
/// <c>X-XSRF-TOKEN</c> header（Angular <c>HttpClient</c> 的內建慣例，值取自 <c>XSRF-TOKEN</c> cookie）。
/// 掛在 <c>/api</c> 群組與 <c>POST /auth/logout</c>；token 與登入身分綁定，所以必須在 authentication 之後執行。
/// </summary>
/// <remarks>
/// endpoint filter 在參數綁定之後才執行：body 格式錯誤時仍會先回綁定的 400，但 handler 一定不會在驗證通過前執行。
/// OIDC 的 <c>/auth/callback</c> 由 authentication middleware 處理，不是 endpoint，這個 filter 不會碰到它。
/// </remarks>
internal sealed class AntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public const string HeaderName = "X-XSRF-TOKEN";

    /// <summary>前端讀取的 request token cookie。名稱是 Angular 的預設值，不能加 <c>__Host-</c> 前綴。</summary>
    public const string RequestTokenCookieName = "XSRF-TOKEN";

    /// <summary>antiforgery 自己的 cookie token，與登入 cookie 一樣用 <c>__Host-</c> 前綴，也不沿用框架預設名稱。</summary>
    public const string CookieTokenName = "__Host-sixjars-af";

    public const string InvalidTokenTitle = "缺少或無效的 XSRF token";

    /// <summary>header 名稱與 cookie token 的屬性；cookie token 只給伺服器比對，所以 HttpOnly。</summary>
    public static void ConfigureOptions(AntiforgeryOptions o)
    {
        o.HeaderName = HeaderName;
        o.Cookie.Name = CookieTokenName;
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.Path = "/";
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        // 安全方法（RFC 9110）不改變狀態，不要求 token；前端第一次載入時也還沒有 token。
        if (!IsSafe(httpContext.Request.Method) && !await antiforgery.IsRequestValidAsync(httpContext))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: InvalidTokenTitle);
        }

        return await next(context);
    }

    private static bool IsSafe(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method);
}
