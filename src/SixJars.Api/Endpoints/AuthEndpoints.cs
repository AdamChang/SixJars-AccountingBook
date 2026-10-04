using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using SixJars.Api.Infrastructure;

namespace SixJars.Api.Endpoints;

internal static class AuthEndpoints
{
    /// <summary>
    /// 登入、登出與拒絕頁（spec §3.5）。不在 <c>/api</c> 底下、不要求登入：轉址到 Google 只在 <c>/auth/login</c> 發生。
    /// </summary>
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/auth");

        auth.MapGet("/login", (string? returnUrl) => Results.Challenge(
            new AuthenticationProperties { RedirectUri = ReturnUrl.Sanitize(returnUrl) }, [AuthenticationSetup.GoogleScheme]));

        // T37：這裡要掛上 antiforgery filter（.AddEndpointFilter<AntiforgeryFilter>()），否則別的網站可以用 form POST 強迫使用者登出。
        auth.MapPost("/logout", async (HttpContext httpContext) =>
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        auth.MapGet("/denied", () => Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "此 Google 帳號不在白名單"));
    }
}
