using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests.Mcp;

/// <summary>
/// /oauth/token（ADR 0013）：code 一次性、綁定 PKCE、client 與 redirect_uri；refresh token 每次輪替，
/// 已輪替掉的舊 token 再出現就撤銷整筆授權（OAuth 2.1 對 public client 的要求）。錯誤一律是 RFC 6749 的 JSON。
/// </summary>
public class TokenEndpointTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Start = new(2026, 10, 30, 1, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(ApiFactory Factory, MutableTimeProvider Clock)> CreateAsync()
    {
        var clock = new MutableTimeProvider(Start);
        var (factory, _) = await OAuthTestClient.CreateFactoryAsync(postgres, Ct, services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
        return (factory, clock);
    }

    [Fact]
    public async Task Code_exchange_issues_tokens_usable_on_mcp()
    {
        var (factory, _) = await CreateAsync();
        await using var _f = factory;
        var client = OAuthTestClient.CreateClient(factory);
        var (verifier, challenge) = OAuthTestClient.NewPkce();
        var code = await OAuthTestClient.AuthorizeAsync(client, challenge, Ct);

        var response = await OAuthTestClient.TokenAsync(client, OAuthTestClient.CodeExchange(code, verifier), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("token_type").GetString().Should().Be("Bearer");
        body.GetProperty("expires_in").GetInt32().Should().Be(3600);
        body.GetProperty("scope").GetString().Should().Be("ledger:read");
        body.GetProperty("refresh_token").GetString().Should().NotBeNullOrEmpty();
        (await InitializeAsync(factory, body.GetProperty("access_token").GetString()!)).Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("code_verifier", "wrong-verifier-wrong-verifier-wrong-verifier")]
    [InlineData("redirect_uri", "http://localhost:3118/callback")]
    [InlineData("client_id", "https://claude.ai/other-client")]
    [InlineData("resource", "https://evil.example/mcp")]
    public async Task Code_exchange_with_mismatched_binding_is_invalid_grant(string name, string value)
    {
        var (factory, _) = await CreateAsync();
        await using var _f = factory;
        var client = OAuthTestClient.CreateClient(factory);
        var (verifier, challenge) = OAuthTestClient.NewPkce();
        var code = await OAuthTestClient.AuthorizeAsync(client, challenge, Ct);
        var form = OAuthTestClient.CodeExchange(code, verifier);
        form[name] = value;

        await ShouldBeErrorAsync(await OAuthTestClient.TokenAsync(client, form, Ct), "invalid_grant");
    }

    [Fact]
    public async Task Expired_code_is_invalid_grant()
    {
        var (factory, clock) = await CreateAsync();
        await using var _f = factory;
        var client = OAuthTestClient.CreateClient(factory);
        var (verifier, challenge) = OAuthTestClient.NewPkce();
        var code = await OAuthTestClient.AuthorizeAsync(client, challenge, Ct);
        clock.Advance(TimeSpan.FromSeconds(61));

        await ShouldBeErrorAsync(await OAuthTestClient.TokenAsync(client, OAuthTestClient.CodeExchange(code, verifier), Ct), "invalid_grant");
    }

    [Fact]
    public async Task Reused_code_is_invalid_grant_and_revokes_first_grant()
    {
        var (factory, _) = await CreateAsync();
        await using var _f = factory;
        var client = OAuthTestClient.CreateClient(factory);
        var (verifier, challenge) = OAuthTestClient.NewPkce();
        var code = await OAuthTestClient.AuthorizeAsync(client, challenge, Ct);
        var first = await ReadTokensAsync(await OAuthTestClient.TokenAsync(client, OAuthTestClient.CodeExchange(code, verifier), Ct));

        await ShouldBeErrorAsync(await OAuthTestClient.TokenAsync(client, OAuthTestClient.CodeExchange(code, verifier), Ct), "invalid_grant");

        (await InitializeAsync(factory, first.Access)).Should().Be(HttpStatusCode.Unauthorized, "code 被重複兌換代表外洩，先前發出的 token 一併失效");
    }

    [Fact]
    public async Task Refresh_rotates_tokens()
    {
        var (factory, clock) = await CreateAsync();
        await using var _f = factory;
        var client = OAuthTestClient.CreateClient(factory);
        var first = await IssueAsync(client);
        clock.Advance(TimeSpan.FromMinutes(70));
        (await InitializeAsync(factory, first.Access)).Should().Be(HttpStatusCode.Unauthorized, "access token 1 小時到期");

        var second = await ReadTokensAsync(await OAuthTestClient.TokenAsync(client, OAuthTestClient.RefreshRequest(first.Refresh), Ct));

        second.Refresh.Should().NotBe(first.Refresh);
        (await InitializeAsync(factory, second.Access)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reused_refresh_token_revokes_grant()
    {
        var (factory, _) = await CreateAsync();
        await using var _f = factory;
        var client = OAuthTestClient.CreateClient(factory);
        var first = await IssueAsync(client);
        var second = await ReadTokensAsync(await OAuthTestClient.TokenAsync(client, OAuthTestClient.RefreshRequest(first.Refresh), Ct));

        await ShouldBeErrorAsync(await OAuthTestClient.TokenAsync(client, OAuthTestClient.RefreshRequest(first.Refresh), Ct), "invalid_grant");

        await ShouldBeErrorAsync(await OAuthTestClient.TokenAsync(client, OAuthTestClient.RefreshRequest(second.Refresh), Ct), "invalid_grant");
        (await InitializeAsync(factory, second.Access)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unknown_refresh_token_is_invalid_grant()
    {
        var (factory, _) = await CreateAsync();
        await using var _f = factory;

        await ShouldBeErrorAsync(
            await OAuthTestClient.TokenAsync(OAuthTestClient.CreateClient(factory, null), OAuthTestClient.RefreshRequest("guess"), Ct), "invalid_grant");
    }

    [Fact]
    public async Task Refresh_with_other_client_id_is_invalid_grant()
    {
        var (factory, _) = await CreateAsync();
        await using var _f = factory;
        var client = OAuthTestClient.CreateClient(factory);
        var first = await IssueAsync(client);
        var form = OAuthTestClient.RefreshRequest(first.Refresh);
        form["client_id"] = "https://claude.ai/other-client";

        await ShouldBeErrorAsync(await OAuthTestClient.TokenAsync(client, form, Ct), "invalid_grant");
    }

    [Fact]
    public async Task Unsupported_grant_type_is_rejected()
    {
        var (factory, _) = await CreateAsync();
        await using var _f = factory;
        var form = new Dictionary<string, string> { ["grant_type"] = "client_credentials" };

        await ShouldBeErrorAsync(await OAuthTestClient.TokenAsync(OAuthTestClient.CreateClient(factory, null), form, Ct), "unsupported_grant_type");
    }

    [Fact]
    public async Task Json_body_is_rejected()
    {
        var (factory, _) = await CreateAsync();
        await using var _f = factory;

        var response = await OAuthTestClient.CreateClient(factory, null)
            .PostAsJsonAsync("/oauth/token", new { grant_type = "authorization_code" }, Ct);

        await ShouldBeErrorAsync(response, "invalid_request");
    }

    private async Task<(string Access, string Refresh)> IssueAsync(HttpClient client)
    {
        var (verifier, challenge) = OAuthTestClient.NewPkce();
        var code = await OAuthTestClient.AuthorizeAsync(client, challenge, Ct);
        return await ReadTokensAsync(await OAuthTestClient.TokenAsync(client, OAuthTestClient.CodeExchange(code, verifier), Ct));
    }

    private static async Task<(string Access, string Refresh)> ReadTokensAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (body.GetProperty("access_token").GetString()!, body.GetProperty("refresh_token").GetString()!);
    }

    private static async Task<HttpStatusCode> InitializeAsync(ApiFactory factory, string accessToken)
    {
        using var request = McpTestTokens.InitializeRequest();
        return (await McpTestTokens.CreateBearerClient(factory, accessToken).SendAsync(request, Ct)).StatusCode;
    }

    private static async Task ShouldBeErrorAsync(HttpResponseMessage response, string error)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("error").GetString().Should().Be(error);
    }
}

/// <summary>可以前進的時鐘：code 與 access token 的到期由注入的 <see cref="TimeProvider"/> 判斷。</summary>
internal sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan delta) => now += delta;
}
