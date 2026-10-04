using System.Net;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using SixJars.Api.Infrastructure;
using SixJars.Domain.Members;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests.Auth;

/// <summary>
/// 正式的 cookie＋Google OIDC 設定（spec §3.5、ADR 0005）。從 <see cref="IOptionsMonitor{TOptions}"/> 取出設定斷言（spike S4），
/// 事件則直接呼叫；不真的連 Google。這裡的 factory 不換成 <see cref="TestAuthHandler"/>。
/// </summary>
public class AuthenticationOptionsTests(PostgresFixture postgres)
{
    private const string DummyClientId = "dummy-client-id";

    /// <summary>明顯是假的值，只在記憶體中設定；真正的值只來自環境變數。</summary>
    private static readonly Dictionary<string, string> GoogleSettings = new()
    {
        ["Authentication:Google:ClientId"] = DummyClientId,
        ["Authentication:Google:ClientSecret"] = "dummy-client-secret",
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Google_and_cookie_options_follow_the_spec()
    {
        await using var factory = await CreateFactoryAsync(GoogleSettings);

        var oidc = factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(AuthenticationSetup.GoogleScheme);
        oidc.Authority.Should().Be("https://accounts.google.com");
        oidc.ResponseType.Should().Be(OpenIdConnectResponseType.Code);
        oidc.Scope.Should().Contain(["openid", "email"]);
        oidc.ClientId.Should().Be(DummyClientId, "client id 讀自 Authentication:Google:ClientId");
        oidc.ClientSecret.Should().Be("dummy-client-secret");
        oidc.MapInboundClaims.Should().BeFalse("claim 名稱要保持 sub、email，HttpCurrentUser 與白名單檢查都依賴它");
        oidc.SignInScheme.Should().Be(CookieAuthenticationDefaults.AuthenticationScheme);
        oidc.CallbackPath.Should().Be(new PathString("/auth/callback"));
        oidc.SaveTokens.Should().BeFalse("cookie 不需要攜帶 Google 的 token");

        var cookie = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        cookie.Cookie.Name.Should().Be(AuthenticationSetup.CookieName);
        cookie.Cookie.HttpOnly.Should().BeTrue();
        cookie.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always);
        cookie.Cookie.SameSite.Should().Be(SameSiteMode.Lax);
        cookie.ExpireTimeSpan.Should().Be(TimeSpan.FromDays(14));
        cookie.SlidingExpiration.Should().BeTrue();
    }

    /// <summary>
    /// SPA 以 XHR 呼叫 API：未登入要回 401 讓前端自己導向 /auth/login，而不是 302 到 Google（XHR 跟不過去）。
    /// Google scheme 有註冊，但不是預設的 challenge scheme。
    /// </summary>
    [Fact]
    public async Task Unauthenticated_api_call_is_401_without_redirect()
    {
        await using var factory = await CreateFactoryAsync(GoogleSettings);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/api/me", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Location.Should().BeNull();
    }

    /// <summary>
    /// 缺 Google 設定時 OIDC handler 會讓每個請求 500，所以正式環境要在啟動時就失敗；
    /// Development（含其他測試）則不註冊 Google scheme，照常啟動，見 <see cref="Cookie_redirect_events_return_status_codes"/>。
    /// </summary>
    [Fact]
    public void Startup_fails_without_google_settings_outside_development()
    {
        using var factory = new ApiFactory(
            "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x", useTestAuthentication: false,
            settings: new Dictionary<string, string> { ["environment"] = "Production" });

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Authentication__Google__ClientId*");
    }

    /// <summary>沒有 Google 設定（Development）也要能啟動，cookie scheme 照樣把轉址換成狀態碼。</summary>
    [Fact]
    public async Task Cookie_redirect_events_return_status_codes()
    {
        await using var factory = await CreateFactoryAsync(settings: null);
        (await factory.CreateClient().GetAsync("/health", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var scheme = await SchemeAsync(factory, CookieAuthenticationDefaults.AuthenticationScheme);

        var login = new DefaultHttpContext();
        await options.Events.RedirectToLogin(new RedirectContext<CookieAuthenticationOptions>(
            login, scheme, options, new AuthenticationProperties(), "https://localhost/Account/Login"));
        var denied = new DefaultHttpContext();
        await options.Events.RedirectToAccessDenied(new RedirectContext<CookieAuthenticationOptions>(
            denied, scheme, options, new AuthenticationProperties(), "https://localhost/Account/AccessDenied"));

        login.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        login.Response.Headers.Location.Should().BeEmpty();
        denied.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        denied.Response.Headers.Location.Should().BeEmpty();
    }

    /// <summary>
    /// 其他 API 測試都靠 <see cref="TestAuthHandler"/>；加入 OIDC 之後，預設的 authenticate／challenge／forbid 仍然要落在 Test scheme，
    /// 否則未登入會被轉到 Google，而不是 401。
    /// </summary>
    [Fact]
    public async Task Test_factory_still_authenticates_with_the_test_scheme()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var schemes = factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();

        (await schemes.GetDefaultAuthenticateSchemeAsync())!.Name.Should().Be(TestAuthHandler.SchemeName);
        (await schemes.GetDefaultChallengeSchemeAsync())!.Name.Should().Be(TestAuthHandler.SchemeName);
        (await schemes.GetDefaultForbidSchemeAsync())!.Name.Should().Be(TestAuthHandler.SchemeName);
        var response = await factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
            .GetAsync("/api/me", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// <c>/auth/login</c> 以 authorization code flow 轉到 Google。discovery 文件以靜態設定取代，不連網；
    /// 轉址時要以 DataProtection 加密 state，金鑰因此寫進資料庫（Cloud Run 換 instance 不會登出所有人）。
    /// </summary>
    [Fact]
    public async Task Login_redirects_to_google_and_persists_data_protection_keys()
    {
        const string authorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        await using var factory = await CreateFactoryAsync(GoogleSettings, services =>
            services.Configure<OpenIdConnectOptions>(AuthenticationSetup.GoogleScheme, o =>
                o.Configuration = new OpenIdConnectConfiguration { AuthorizationEndpoint = authorizationEndpoint }));
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/auth/login?returnUrl=%2Ftransactions", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.Should().StartWith(authorizationEndpoint);
        location.Should().Contain($"client_id={DummyClientId}").And.Contain("response_type=code").And.Contain("email")
            .And.Contain(Uri.EscapeDataString("https://localhost/auth/callback"));
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<SixJarsDbContext>().DataProtectionKeys.CountAsync(Ct))
            .Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Token_validated_event_runs_the_whitelist()
    {
        await using var factory = await CreateFactoryAsync(GoogleSettings);
        var book = await factory.SeedBookAsync(Ct, ownerSubject: null);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
            db.BookMembers.Add(BookMember.Create(book.Id, "member@example.com", BookRole.Owner, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(Ct);
        }

        var member = await RaiseTokenValidatedAsync(factory, "member-sub", "member@example.com");
        var stranger = await RaiseTokenValidatedAsync(factory, "stranger-sub", "stranger@example.com");

        member.Result.Should().BeNull("通過白名單時不介入，交給 cookie 發出登入狀態");
        stranger.Result.Should().NotBeNull("不在白名單就拒絕登入，不發 cookie");
        stranger.Result!.Failure.Should().NotBeNull();
    }

    [Fact]
    public async Task Remote_failure_redirects_to_denied_page()
    {
        await using var factory = await CreateFactoryAsync(GoogleSettings);
        var options = factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(AuthenticationSetup.GoogleScheme);
        var httpContext = new DefaultHttpContext();
        var context = new RemoteFailureContext(
            httpContext, await SchemeAsync(factory, AuthenticationSetup.GoogleScheme), options, new AuthenticationFailureException("不在白名單"));

        await options.Events.RemoteFailure(context);

        context.Result!.Handled.Should().BeTrue();
        httpContext.Response.Headers.Location.ToString().Should().Be("/auth/denied");
    }

    private async Task<ApiFactory> CreateFactoryAsync(
        IReadOnlyDictionary<string, string>? settings, Action<IServiceCollection>? testServices = null) =>
        new(await postgres.CreateConnectionStringAsync(Ct), testServices, useTestAuthentication: false, settings);

    private static async Task<AuthenticationScheme> SchemeAsync(ApiFactory factory, string name) =>
        (await factory.Services.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync(name))!;

    private static async Task<TokenValidatedContext> RaiseTokenValidatedAsync(ApiFactory factory, string subject, string email)
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(AuthenticationSetup.GoogleScheme);
        await using var scope = factory.Services.CreateAsyncScope();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", subject), new Claim("email", email), new Claim("email_verified", "true")], "Google"));
        var context = new TokenValidatedContext(
            new DefaultHttpContext { RequestServices = scope.ServiceProvider },
            await SchemeAsync(factory, AuthenticationSetup.GoogleScheme), options, principal, new AuthenticationProperties());

        await options.Events.TokenValidated(context);
        return context;
    }
}
