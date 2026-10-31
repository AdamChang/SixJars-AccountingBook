using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests.Mcp;

/// <summary>
/// /mcp 的門（ADR 0013）：只接受 MCP bearer token，未認證時以 401 指向 protected resource metadata，
/// Claude 由 metadata 找到授權伺服器。cookie 身分與 bearer token 互不通用。
/// </summary>
public class McpDiscoveryTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Mcp_without_token_returns_401_with_resource_metadata()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);

        using var request = McpTestTokens.InitializeRequest();
        var response = await McpTestTokens.CreateBearerClient(factory, null).SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().Should()
            .Contain("resource_metadata=\"https://localhost/.well-known/oauth-protected-resource/mcp\"");
    }

    [Fact]
    public async Task Mcp_with_cookie_identity_only_returns_401()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);

        using var request = McpTestTokens.InitializeRequest();
        var response = await factory.CreateSignedInClient().SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Mcp_with_valid_token_initializes()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var (_, token) = await McpTestTokens.IssueAsync(factory, Ct);

        using var request = McpTestTokens.InitializeRequest();
        var response = await McpTestTokens.CreateBearerClient(factory, token).SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("\"serverInfo\"");
    }

    [Fact]
    public async Task Mcp_with_revoked_grant_returns_401()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var (grant, token) = await McpTestTokens.IssueAsync(factory, Ct);
        await McpTestTokens.RevokeAsync(factory, grant.Id, Ct);

        using var request = McpTestTokens.InitializeRequest();
        var response = await McpTestTokens.CreateBearerClient(factory, token).SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Mcp_with_token_for_other_audience_returns_401()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var (_, token) = await McpTestTokens.IssueAsync(factory, Ct, audience: "https://evil.example/mcp");

        using var request = McpTestTokens.InitializeRequest();
        var response = await McpTestTokens.CreateBearerClient(factory, token).SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Mcp_with_garbage_token_returns_401()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);

        using var request = McpTestTokens.InitializeRequest();
        var response = await McpTestTokens.CreateBearerClient(factory, "not-a-token").SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Bearer_token_is_not_accepted_on_api()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var (_, token) = await McpTestTokens.IssueAsync(factory, Ct);

        var response = await McpTestTokens.CreateBearerClient(factory, token).GetAsync("/api/books", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Protected_resource_metadata_names_resource_and_issuer()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);

        var metadata = await McpTestTokens.CreateBearerClient(factory, null)
            .GetFromJsonAsync<JsonElement>("/.well-known/oauth-protected-resource/mcp", Ct);

        metadata.GetProperty("resource").GetString().Should().Be("https://localhost/mcp");
        metadata.GetProperty("authorization_servers").EnumerateArray().Select(e => e.GetString()).Should().Equal("https://localhost");
        metadata.GetProperty("scopes_supported").EnumerateArray().Select(e => e.GetString()).Should().Equal("ledger:read");
    }

    [Fact]
    public async Task Authorization_server_metadata_advertises_cimd_and_s256()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);

        var metadata = await McpTestTokens.CreateBearerClient(factory, null)
            .GetFromJsonAsync<JsonElement>("/.well-known/oauth-authorization-server", Ct);

        metadata.GetProperty("issuer").GetString().Should().Be("https://localhost");
        metadata.GetProperty("authorization_endpoint").GetString().Should().Be("https://localhost/oauth/authorize");
        metadata.GetProperty("token_endpoint").GetString().Should().Be("https://localhost/oauth/token");
        metadata.GetProperty("client_id_metadata_document_supported").GetBoolean().Should().BeTrue();
        Strings(metadata, "token_endpoint_auth_methods_supported").Should().Equal("none");
        Strings(metadata, "code_challenge_methods_supported").Should().Equal("S256");
        Strings(metadata, "grant_types_supported").Should().BeEquivalentTo("authorization_code", "refresh_token");
        Strings(metadata, "response_types_supported").Should().Equal("code");
        Strings(metadata, "scopes_supported").Should().Equal("ledger:read");
        metadata.TryGetProperty("registration_endpoint", out _).Should().BeFalse("只支援 CIMD，不開放動態註冊（ADR 0013）");
    }

    private static IEnumerable<string?> Strings(JsonElement element, string name) =>
        element.GetProperty(name).EnumerateArray().Select(e => e.GetString());
}
