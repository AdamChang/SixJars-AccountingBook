using Microsoft.AspNetCore.Antiforgery;
using SixJars.Api.Infrastructure;

namespace SixJars.Api.Endpoints;

internal static class AntiforgeryEndpoints
{
    /// <summary>
    /// 發出 XSRF token（ADR 0005）。在 <c>/api</c> 底下、要求登入：token 與登入身分綁定，前端登入後呼叫一次即可。
    /// <c>XSRF-TOKEN</c> 不能 HttpOnly，Angular <c>HttpClient</c> 要讀取它放進 <c>X-XSRF-TOKEN</c> header。
    /// </summary>
    public static RouteGroupBuilder MapAntiforgeryEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/antiforgery/token", (HttpContext httpContext, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(httpContext);
            httpContext.Response.Cookies.Append(AntiforgeryFilter.RequestTokenCookieName, tokens.RequestToken!, new CookieOptions
            {
                HttpOnly = false,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/",
            });
            return Results.NoContent();
        });
        return api;
    }
}
