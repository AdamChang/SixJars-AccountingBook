using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using SixJars.Infrastructure.Persistence;

namespace SixJars.Api.Infrastructure;

/// <summary>
/// BFF 登入（ADR 0005、spec §3.5）：ASP.NET Core 自己跑 Google OIDC 的 authorization code flow，成功後發出同源的 HttpOnly cookie；
/// 前端完全不經手 token。
/// </summary>
internal static class AuthenticationSetup
{
    public const string GoogleScheme = "Google";

    /// <summary>
    /// 登入 cookie 的名稱。<c>__Host-</c> 前綴讓瀏覽器強制要求 Secure、Path=/、不帶 Domain，
    /// 子網域無法覆寫；也不沿用框架預設的 <c>.AspNetCore.Cookies</c>，不透露後端技術。
    /// </summary>
    public const string CookieName = "__Host-sixjars-auth";

    /// <summary>
    /// cookie 是預設 scheme；Google 只在 <c>/auth/login</c> 明確 challenge 時使用，所以 API 未登入一律回 401，不會轉址。
    /// client id 與 secret 讀自 <c>Authentication:Google:ClientId</c>／<c>ClientSecret</c>
    /// （環境變數 <c>Authentication__Google__ClientId</c>／<c>Authentication__Google__ClientSecret</c>），repo 裡沒有任何值。
    /// </summary>
    /// <remarks>
    /// OIDC options 並不是「延遲到登入才驗證」：authentication middleware 每個請求都會初始化 remote scheme 的 handler
    /// （檢查是不是 callback 路徑），缺 ClientId 時每個請求都會 500。所以沒有設定時：
    /// Development（含測試）不註冊 Google scheme，其他功能照常；其他環境在啟動時就失敗，不讓設定錯誤的服務上線。
    /// </remarks>
    public static IServiceCollection AddSixJarsAuthentication(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var clientId = configuration["Authentication:Google:ClientId"];
        var clientSecret = configuration["Authentication:Google:ClientSecret"];
        var googleConfigured = !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret);
        if (!googleConfigured && !environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "未設定 Google 登入：請設定環境變數 Authentication__Google__ClientId 與 Authentication__Google__ClientSecret。");
        }

        services.AddScoped<GoogleSignInValidator>();
        var authentication = services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(ConfigureCookie);
        if (googleConfigured)
        {
            authentication.AddOpenIdConnect(GoogleScheme, o => ConfigureGoogle(o, clientId!, clientSecret!));
        }

        // cookie 加密金鑰存在 DB：Cloud Run 換 instance 時使用者不會被登出（ADR 0005）。
        // 固定 application name，金鑰才不會因為部署路徑不同而被隔離。
        services.AddDataProtection()
            .SetApplicationName("SixJars")
            .PersistKeysToDbContext<SixJarsDbContext>();
        return services;
    }

    private static void ConfigureCookie(CookieAuthenticationOptions o)
    {
        o.Cookie.Name = CookieName;
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromDays(14);
        o.SlidingExpiration = true;
        // SPA 以 XHR 呼叫 API，302 跟不過去；回狀態碼讓前端自己導向 /auth/login（spec §4「API 不轉址」）。
        o.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }

    private static void ConfigureGoogle(OpenIdConnectOptions o, string clientId, string clientSecret)
    {
        o.Authority = "https://accounts.google.com";
        o.ClientId = clientId;
        o.ClientSecret = clientSecret;
        o.ResponseType = OpenIdConnectResponseType.Code;
        o.Scope.Add("email");
        // claim 名稱保持 sub、email、email_verified，不轉成 WS-Fed 的長名稱；HttpCurrentUser 與白名單檢查都依賴它。
        o.MapInboundClaims = false;
        o.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        // 登入相關的路徑都在 /auth 底下；Google OAuth client 的 redirect URI 要設成 https://<網域>/auth/callback。
        o.CallbackPath = "/auth/callback";
        o.Events.OnTokenValidated = async ctx =>
        {
            var validator = ctx.HttpContext.RequestServices.GetRequiredService<GoogleSignInValidator>();
            if (!await validator.ValidateAsync(ctx.Principal!, ctx.HttpContext.RequestAborted))
            {
                ctx.Fail("不在白名單");
            }
        };
        // 白名單拒絕、使用者在 Google 取消同意等遠端失敗，一律導向說明頁，不讓例外變成 500。
        o.Events.OnRemoteFailure = ctx =>
        {
            ctx.Response.Redirect("/auth/denied");
            ctx.HandleResponse();
            return Task.CompletedTask;
        };
    }
}
