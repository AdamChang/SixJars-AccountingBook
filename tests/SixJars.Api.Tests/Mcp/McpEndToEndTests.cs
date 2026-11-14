using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SixJars.Application.Transactions;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests.Mcp;

/// <summary>
/// 從 401 一路到 tool 呼叫（ADR 0013），照 Claude 的做法：每一步的網址都取自上一步的回應（WWW-Authenticate → protected resource
/// metadata → authorization server metadata），不寫死路徑，證明 discovery 足以讓 client 自己找到授權伺服器。
/// </summary>
public partial class McpEndToEndTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Claude_like_client_discovers_authorizes_calls_tool_and_refreshes()
    {
        var (factory, _) = await OAuthTestClient.CreateFactoryAsync(postgres, Ct);
        await using var _f = factory;
        var book = await factory.SeedBookAsync(Ct);
        var browser = OAuthTestClient.CreateClient(factory);
        var claude = OAuthTestClient.CreateClient(factory, subject: null);

        // 1. 未帶 token → 401，取出 resource_metadata。
        using var initialize = McpTestTokens.InitializeRequest();
        var unauthorized = await claude.SendAsync(initialize, Ct);
        unauthorized.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var resourceMetadataUrl = ResourceMetadata().Match(unauthorized.Headers.WwwAuthenticate.ToString()).Groups["url"].Value;

        // 2. protected resource metadata → 授權伺服器 → RFC 8414 metadata。
        var resource = await claude.GetFromJsonAsync<JsonElement>(resourceMetadataUrl, Ct);
        var issuer = resource.GetProperty("authorization_servers")[0].GetString()!;
        var server = await claude.GetFromJsonAsync<JsonElement>($"{issuer}/.well-known/oauth-authorization-server", Ct);
        server.GetProperty("client_id_metadata_document_supported").GetBoolean().Should().BeTrue();

        // 3. 使用者在瀏覽器同意 → code。
        var (verifier, challenge) = OAuthTestClient.NewPkce();
        var parameters = OAuthTestClient.AuthorizeParameters(challenge);
        parameters["resource"] = resource.GetProperty("resource").GetString();
        var consent = await OAuthTestClient.ConsentAsync(browser, parameters, Ct);
        var callback = QueryHelpers.ParseQuery(consent.Headers.Location!.Query);
        callback["iss"].ToString().Should().Be(issuer);

        // 4. Claude 以 code 換 token（token_endpoint 取自 metadata）。
        var tokenEndpoint = server.GetProperty("token_endpoint").GetString()!;
        var issued = await claude.PostAsync(tokenEndpoint, new FormUrlEncodedContent(OAuthTestClient.CodeExchange(callback["code"]!, verifier)), Ct);
        var tokens = await issued.Content.ReadFromJsonAsync<JsonElement>(Ct);

        // 5. 呼叫 tool。
        (await ListBookIdsAsync(factory, tokens.GetProperty("access_token").GetString()!)).Should().Equal(book.Id.Value);

        // 6. refresh 後再呼叫一次。
        var refreshed = await claude.PostAsync(tokenEndpoint,
            new FormUrlEncodedContent(OAuthTestClient.RefreshRequest(tokens.GetProperty("refresh_token").GetString()!)), Ct);
        var newTokens = await refreshed.Content.ReadFromJsonAsync<JsonElement>(Ct);
        (await ListBookIdsAsync(factory, newTokens.GetProperty("access_token").GetString()!)).Should().Equal(book.Id.Value);
    }

    [Fact]
    public async Task Write_consent_to_add_and_read_back_via_api()
    {
        var (factory, _) = await OAuthTestClient.CreateFactoryAsync(postgres, Ct);
        await using var _f = factory;
        var book = await factory.SeedBookAsync(Ct);
        var browser = OAuthTestClient.CreateClient(factory);
        var claude = OAuthTestClient.CreateClient(factory, subject: null);

        // 同意頁勾選「也允許新增交易」→ code → token。
        var (verifier, challenge) = OAuthTestClient.NewPkce();
        var consent = await OAuthTestClient.ConsentAsync(browser, OAuthTestClient.AuthorizeParameters(challenge), Ct, allowWrite: true);
        consent.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var code = QueryHelpers.ParseQuery(consent.Headers.Location!.Query)["code"].ToString();
        var issued = await OAuthTestClient.TokenAsync(claude, OAuthTestClient.CodeExchange(code, verifier), Ct);
        var tokens = await issued.Content.ReadFromJsonAsync<JsonElement>(Ct);
        tokens.GetProperty("scope").GetString().Should().Contain("ledger:write");

        // MCP 記一筆支出。
        await using var mcp = await ConnectAsync(factory, tokens.GetProperty("access_token").GetString()!);
        var result = await mcp.CallToolAsync("add_transaction", new Dictionary<string, object?>
        {
            ["bookId"] = book.Id.Value,
            ["kind"] = "Expense",
            ["date"] = "2026-01-15",
            ["amount"] = 120,
            ["accountId"] = book.FindAccount("現金")!.Id.Value,
            ["categoryId"] = book.FindCategory("主食", "午餐")!.Id.Value,
            ["sourceId"] = "<e2e-1@mail>",
        }, cancellationToken: Ct);
        result.IsError.Should().NotBe(true, string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text)));

        // 用 cookie API 讀回。
        var member = await factory.CreateMemberClientAsync();
        var list = await member.GetFromJsonAsync<List<TransactionDto>>(
            $"/api/books/{book.Id.Value}/transactions?from=2026-01-01&to=2026-01-31", ApiJson.Options, Ct);
        var stored = list.Should().ContainSingle().Subject;
        stored.CreatedVia.Should().Be("mcp:claude.ai");
        stored.ExternalRef.Should().Be("<e2e-1@mail>");
        stored.Amount.Should().Be(-120m);
    }

    private static async Task<McpClient> ConnectAsync(ApiFactory factory, string accessToken)
    {
        var http = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await McpClient.CreateAsync(new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri("https://localhost/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http, NullLoggerFactory.Instance, ownsHttpClient: true), cancellationToken: Ct);
    }

    private static async Task<IEnumerable<Guid>> ListBookIdsAsync(ApiFactory factory, string accessToken)
    {
        await using var client = await ConnectAsync(factory, accessToken);
        (await client.ListToolsAsync(cancellationToken: Ct)).Select(t => t.Name).Should().NotContain("add_transaction");
        var result = await client.CallToolAsync("list_books", new Dictionary<string, object?>(), cancellationToken: Ct);
        result.IsError.Should().NotBe(true);
        var json = JsonDocument.Parse(string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text))).RootElement;
        return json.EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).ToList();
    }

    [GeneratedRegex("resource_metadata=\"(?<url>[^\"]+)\"")]
    private static partial Regex ResourceMetadata();
}
