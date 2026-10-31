using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Authorization;
using SixJars.Api.Infrastructure;
using SixJars.Api.Mcp;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

/// <summary>
/// 段 E 的整組安全性慣例（ADR 0005）：<c>/api/**</c> 一律要求登入，會改變狀態的請求一律要求 XSRF token。
/// 以列舉實際註冊的 endpoint 檢查，日後新增的 endpoint 忘了掛上就會失敗。
/// </summary>
public class SecurityConventionTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// 不在 <c>/api</c> 底下的只有 <c>/health</c>、<c>/health/ready</c>、<c>/auth/**</c>，加上遠端 MCP 的
    /// <c>/oauth/**</c>、<c>/.well-known/**</c>（OAuth 端點本身就給外部 client 匿名呼叫）與 <c>/mcp</c>（只接受 bearer，見下一個測試；ADR 0013）。
    /// 其他路徑一律要掛在 <c>/api</c> 群組，才會套到 <c>RequireAuthorization()</c>。
    /// </summary>
    [Fact]
    public async Task Every_api_endpoint_requires_sign_in()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var endpoints = ApiEndpoints.Under(factory, "/api/");
        endpoints.Should().HaveCountGreaterThanOrEqualTo(18, "帳本範圍至少 15 個，加上 /api/me、/api/books 與 /api/antiforgery/token");
        ApiEndpoints.All(factory).Select(e => e.RoutePattern.RawText!).Except(endpoints.Select(e => e.RoutePattern.RawText!))
            .Should().OnlyContain(path => path == "/health" || path == "/health/ready" || path.StartsWith("/auth/", StringComparison.Ordinal)
                || path.StartsWith("/oauth/", StringComparison.Ordinal) || path.StartsWith("/.well-known/", StringComparison.Ordinal)
                || path.StartsWith(McpSetup.Path, StringComparison.Ordinal));

        List<string> leaks = [];
        foreach (var (method, url) in ApiEndpoints.Calls(endpoints, book.Id.Value))
        {
            using var request = ApiEndpoints.Request(method, url);
            var response = await anonymous.SendAsync(request, Ct);
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                leaks.Add($"{method} {url} → {(int)response.StatusCode}");
            }
        }

        leaks.Should().BeEmpty();
    }

    /// <summary>
    /// /mcp 的每個 endpoint 都要套用只認 MCP bearer 的 policy：預設 scheme 是 cookie，少了它瀏覽器帶著 cookie 就能打 MCP（ADR 0013）。
    /// </summary>
    [Fact]
    public async Task Every_mcp_endpoint_requires_mcp_bearer_policy()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);

        var endpoints = ApiEndpoints.Under(factory, McpSetup.Path);

        endpoints.Should().NotBeEmpty();
        endpoints.Should().OnlyContain(e => e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy == McpSetup.PolicyName));
    }

    /// <summary>
    /// 以帳本擁有者登入、但不帶 token：拿掉 filter 時這些請求會是 201、404、422 或 validation 的 400，
    /// 所以除了 400 之外還要比對 ProblemDetails 的標題，確定是 XSRF 檢查擋下的。
    /// </summary>
    [Fact]
    public async Task Every_unsafe_api_endpoint_requires_xsrf_token()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var member = factory.CreateSignedInClient();
        var calls = ApiEndpoints.Calls(ApiEndpoints.Under(factory, "/api/"), book.Id.Value)
            .Where(c => !HttpMethods.IsGet(c.Method) && !HttpMethods.IsHead(c.Method))
            .ToList();
        calls.Should().HaveCountGreaterThanOrEqualTo(11, "帳本設定 4 個、交易 3 個、預定支出 4 個（含付款）");

        List<string> leaks = [];
        foreach (var (method, url) in calls)
        {
            using var request = ApiEndpoints.Request(method, url);
            var response = await member.SendAsync(request, Ct);
            var title = response.Content.Headers.ContentType?.MediaType == "application/problem+json"
                ? (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!.Title
                : null;
            if (response.StatusCode != HttpStatusCode.BadRequest || title != AntiforgeryFilter.InvalidTokenTitle)
            {
                leaks.Add($"{method} {url} → {(int)response.StatusCode} {title}");
            }
        }

        leaks.Should().BeEmpty();
    }
}
