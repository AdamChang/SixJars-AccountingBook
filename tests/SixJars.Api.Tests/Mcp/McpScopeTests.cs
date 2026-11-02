using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SixJars.Api.Infrastructure;
using SixJars.Application.Common;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests.Mcp;

/// <summary>
/// 寫入 scope 的授權邊界：授予的 scope 只來自同意頁的核取方塊（hidden 欄位使用者可改），
/// access token 每個 scope 一個 claim（否則讀寫 token 反而打不過唯讀 policy），並帶出「經由」。
/// </summary>
public class McpScopeTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Consent_page_offers_write_checkbox_unchecked_with_warnings()
    {
        var (factory, _) = await OAuthTestClient.CreateFactoryAsync(postgres, Ct);
        await using var _f = factory;
        var (_, challenge) = OAuthTestClient.NewPkce();

        var page = await OAuthTestClient.CreateClient(factory)
            .GetAsync(OAuthTestClient.AuthorizeUrl(OAuthTestClient.AuthorizeParameters(challenge)), Ct);
        var html = await page.Content.ReadAsStringAsync(Ct);

        page.StatusCode.Should().Be(HttpStatusCode.OK);
        html.Should().Contain("name=\"allow_write\"");
        var checkbox = Regex.Match(html, "<input[^>]*name=\"allow_write\"[^>]*>").Value;
        checkbox.Should().NotBeEmpty().And.NotContain("checked", "寫入權限必須由使用者主動勾選，不能預設勾選");
        var formStart = html.IndexOf("<form", StringComparison.Ordinal);
        var formEnd = html.IndexOf("</form>", StringComparison.Ordinal);
        var checkboxAt = html.IndexOf(checkbox, StringComparison.Ordinal);
        formStart.Should().BeGreaterThanOrEqualTo(0);
        checkboxAt.Should().BeInRange(formStart, formEnd, "核取方塊必須在 <form> 內，瀏覽器才會送出 allow_write");
        html.Should().Contain("不能修改或刪除").And.Contain("每天最多 15 筆").And.Contain("claude.ai");
    }

    [Fact]
    public async Task Consent_without_write_grants_read_only()
    {
        var (factory, _) = await OAuthTestClient.CreateFactoryAsync(postgres, Ct);
        await using var _f = factory;

        var response = await ExchangeAsync(factory, c => OAuthTestClient.AuthorizeParameters(c), allowWrite: false);

        response.GetProperty("scope").GetString().Should().Be("ledger:read");
    }

    [Fact]
    public async Task Consent_with_write_grants_read_and_write()
    {
        var (factory, _) = await OAuthTestClient.CreateFactoryAsync(postgres, Ct);
        await using var _f = factory;

        var response = await ExchangeAsync(factory, c => OAuthTestClient.AuthorizeParameters(c), allowWrite: true);

        response.GetProperty("scope").GetString().Should().Be("ledger:read ledger:write");
        await using var scope = factory.Services.CreateAsyncScope();
        var grant = await scope.ServiceProvider.GetRequiredService<SixJarsDbContext>().McpGrants.AsNoTracking().SingleAsync(Ct);
        grant.Scope.Should().Be("ledger:read ledger:write");
    }

    [Fact]
    public async Task Requesting_write_scope_without_checkbox_grants_read_only()
    {
        var (factory, _) = await OAuthTestClient.CreateFactoryAsync(postgres, Ct);
        await using var _f = factory;

        var response = await ExchangeAsync(factory, challenge =>
        {
            var parameters = OAuthTestClient.AuthorizeParameters(challenge);
            parameters["scope"] = "ledger:read ledger:write";
            return parameters;
        }, allowWrite: false);

        response.GetProperty("scope").GetString().Should().Be("ledger:read", "請求的 scope 不等於授予的 scope，授予只看核取方塊");
    }

    [Fact]
    public async Task Unknown_scope_is_rejected()
    {
        var (factory, _) = await OAuthTestClient.CreateFactoryAsync(postgres, Ct);
        await using var _f = factory;
        var (_, challenge) = OAuthTestClient.NewPkce();
        var parameters = OAuthTestClient.AuthorizeParameters(challenge);
        parameters["scope"] = "ledger:admin";

        var response = await OAuthTestClient.CreateClient(factory).GetAsync(OAuthTestClient.AuthorizeUrl(parameters), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        QueryHelpers.ParseQuery(response.Headers.Location!.Query)["error"].ToString().Should().Be("invalid_scope");
    }

    [Fact]
    public async Task Read_write_token_still_passes_read_policy()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var (_, token) = await McpTestTokens.IssueAsync(factory, Ct, scope: "ledger:read ledger:write");
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri("https://localhost/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            McpTestTokens.CreateBearerClient(factory, token), NullLoggerFactory.Instance, ownsHttpClient: true);
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: Ct);

        var result = await client.CallToolAsync("list_books", new Dictionary<string, object?>(), cancellationToken: Ct);

        result.IsError.Should().NotBe(true);
        string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text)).Should().Contain(book.Id.Value.ToString());
    }

    [Fact]
    public void Http_current_user_reads_via_claim()
    {
        ICurrentUser user = new HttpCurrentUser(AccessorWith(new Claim("sub", "u1"), new Claim("via", "mcp:claude.ai")));

        user.Via.Should().Be("mcp:claude.ai");
    }

    [Fact]
    public void Http_current_user_via_is_null_without_claim()
    {
        ICurrentUser user = new HttpCurrentUser(AccessorWith(new Claim("sub", "u1")));

        user.Via.Should().BeNull();
    }

    private static HttpContextAccessor AccessorWith(params Claim[] claims) => new()
    {
        HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) },
    };

    /// <summary>走完 authorize（同意）→ token，回傳 token 回應的 JSON。</summary>
    private static async Task<JsonElement> ExchangeAsync(
        ApiFactory factory, Func<string, Dictionary<string, string?>> parameters, bool allowWrite)
    {
        var (verifier, challenge) = OAuthTestClient.NewPkce();
        var browser = OAuthTestClient.CreateClient(factory);
        var consent = await OAuthTestClient.ConsentAsync(browser, parameters(challenge), Ct, allowWrite: allowWrite);
        consent.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var code = QueryHelpers.ParseQuery(consent.Headers.Location!.Query)["code"].ToString();

        var token = await OAuthTestClient.TokenAsync(OAuthTestClient.CreateClient(factory, subject: null), OAuthTestClient.CodeExchange(code, verifier), Ct);

        token.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await token.Content.ReadAsStringAsync(Ct)).RootElement;
    }
}
