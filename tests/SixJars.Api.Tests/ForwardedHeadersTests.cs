using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using SixJars.Api.Infrastructure;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

/// <summary>
/// Cloud Run 在前端終止 TLS，app 收到的是 http；靠 <c>X-Forwarded-Proto</c> 才知道原始請求是 https（段 E 留給 T44 的必要事項）。
/// 測試以 http 的 client 送出，只靠 header 讓 <c>Request.IsHttps</c> 成立，從兩個實際依賴它的行為觀察。
/// </summary>
public class ForwardedHeadersTests(PostgresFixture postgres)
{
    private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>OIDC 的 redirect_uri 由請求的 scheme 組成：沒有 header 是 http，Google 會拒絕（只登記了 https 的 callback）。</summary>
    [Fact]
    public async Task Forwarded_proto_makes_oidc_redirect_uri_https()
    {
        await using var factory = new ApiFactory(
            await postgres.CreateConnectionStringAsync(Ct),
            services =>
            {
                FromCloudRunFrontEnd(services);
                services.Configure<OpenIdConnectOptions>(AuthenticationSetup.GoogleScheme, o =>
                    o.Configuration = new OpenIdConnectConfiguration { AuthorizationEndpoint = AuthorizationEndpoint });
            },
            useTestAuthentication: false,
            new Dictionary<string, string>
            {
                ["Authentication:Google:ClientId"] = "dummy-client-id",
                ["Authentication:Google:ClientSecret"] = "dummy-client-secret",
            });
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
        });

        var direct = await client.GetAsync("/auth/login", Ct);
        using var forwarded = new HttpRequestMessage(HttpMethod.Get, "/auth/login");
        forwarded.Headers.Add("X-Forwarded-Proto", "https");
        forwarded.Headers.Add("X-Forwarded-For", "203.0.113.7");
        var viaProxy = await client.SendAsync(forwarded, Ct);

        direct.Headers.Location!.ToString().Should().Contain(Uri.EscapeDataString("http://localhost/auth/callback"), "對照組：沒有 header 就是 http");
        viaProxy.StatusCode.Should().Be(HttpStatusCode.Redirect);
        viaProxy.Headers.Location!.ToString().Should().StartWith(AuthorizationEndpoint)
            .And.Contain(Uri.EscapeDataString("https://localhost/auth/callback"));
    }

    /// <summary>antiforgery 的 cookie 是 SecurePolicy=Always：在 app 眼中不是 https 的請求，發 token 時會擲例外變成 500。</summary>
    [Fact]
    public async Task Forwarded_proto_lets_antiforgery_issue_tokens_behind_tls_proxy()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct, FromCloudRunFrontEnd);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost") });
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubjectHeader, ApiFactory.DefaultSubject);

        var direct = await client.GetAsync("/api/antiforgery/token", Ct);
        using var forwarded = new HttpRequestMessage(HttpMethod.Get, "/api/antiforgery/token");
        forwarded.Headers.Add("X-Forwarded-Proto", "https");
        var viaProxy = await client.SendAsync(forwarded, Ct);

        direct.StatusCode.Should().Be(HttpStatusCode.InternalServerError, "對照組：直接以 http 呼叫");
        viaProxy.StatusCode.Should().Be(HttpStatusCode.NoContent);
        viaProxy.Headers.GetValues("Set-Cookie").Should().Contain(c => c.StartsWith(AntiforgeryFilter.CookieTokenName + "=", StringComparison.Ordinal));
    }

    /// <summary>
    /// TestServer 的連線沒有 RemoteIpAddress，ForwardedHeaders 會略過 KnownProxies 的檢查；
    /// 照 Cloud Run 的實況把來源設成一個非 loopback 的 IP（GFE），沒清空 KnownProxies 時 header 才會被忽略。
    /// </summary>
    private static void FromCloudRunFrontEnd(IServiceCollection services) =>
        services.AddTransient<IStartupFilter>(_ => new RemoteIpStartupFilter(IPAddress.Parse("169.254.1.1")));

    /// <summary>在 app 的 middleware（含 UseForwardedHeaders）之前設定連線的來源 IP。</summary>
    private sealed class RemoteIpStartupFilter(IPAddress remoteIp) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = remoteIp;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
