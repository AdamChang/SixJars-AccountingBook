using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using SixJars.Api.Mcp.OAuth;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests.Mcp;

/// <summary>
/// /oauth/authorize（ADR 0013）：以 CIMD 辨識 client，沿用 Google cookie 登入，使用者在同意頁明確同意後才發 code。
/// client_id 或 redirect_uri 不可信時絕不轉址（否則成了 open redirect），其他錯誤依 RFC 6749 §4.1.2.1 轉回 client。
/// </summary>
public class AuthorizeEndpointTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Anonymous_user_is_sent_to_google_sign_in_and_back()
    {
        var (factory, _) = await OAuthTestClient.CreateFactoryAsync(postgres, Ct);
        await using var _f = factory;
        var url = OAuthTestClient.AuthorizeUrl(OAuthTestClient.AuthorizeParameters("challenge"));

        var response = await OAuthTestClient.CreateClient(factory, subject: null).GetAsync(url, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.OriginalString;
        location.Should().StartWith("/auth/login?returnUrl=");
        Uri.UnescapeDataString(location["/auth/login?returnUrl=".Length..]).Should().Be(url);
    }

    [Fact]
    public async Task Signed_in_user_sees_consent_page_naming_client_host()
    {
        var (factory, _) = await OAuthTestClient.CreateFactoryAsync(postgres, Ct);
        await using var _f = factory;

        var response = await OAuthTestClient.CreateClient(factory)
            .GetAsync(OAuthTestClient.AuthorizeUrl(OAuthTestClient.AuthorizeParameters("challenge")), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        response.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("frame-ancestors 'none'");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var html = await response.Content.ReadAsStringAsync(Ct);
        html.Should().Contain("claude.ai").And.Contain("__RequestVerificationToken").And.Contain(TestAuthHandler.EmailOf(ApiFactory.DefaultSubject));
    }

    [Fact]
    public async Task Client_id_outside_allowed_hosts_is_rejected_without_fetching()
    {
        var (factory, fetcher) = await OAuthTestClient.CreateFactoryAsync(postgres, Ct);
        await using var _f = factory;
        var parameters = OAuthTestClient.AuthorizeParameters("challenge");
        parameters["client_id"] = "https://evil.example/client.json";

        var response = await OAuthTestClient.CreateClient(factory).GetAsync(OAuthTestClient.AuthorizeUrl(parameters), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.Location.Should().BeNull();
        fetcher.Calls.Should().Be(0, "白名單外的 host 不能被當成 SSRF 跳板");
    }

    [Theory]
    [InlineData("http://claude.ai/oauth/mcp-oauth-client-metadata")]
    [InlineData("not-a-url")]
    public async Task Non_https_client_id_is_rejected(string clientId)
    {
        var (factory, fetcher) = await OAuthTestClient.CreateFactoryAsync(postgres, Ct);
        await using var _f = factory;
        var parameters = OAuthTestClient.AuthorizeParameters("challenge");
        parameters["client_id"] = clientId;

        var response = await OAuthTestClient.CreateClient(factory).GetAsync(OAuthTestClient.AuthorizeUrl(parameters), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        fetcher.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Unregistered_redirect_uri_is_rejected_without_redirect()
    {
        var (factory, _) = await OAuthTestClient.CreateFactoryAsync(postgres, Ct);
        await using var _f = factory;
        var parameters = OAuthTestClient.AuthorizeParameters("challenge");
        parameters["redirect_uri"] = "https://evil.example/callback";

        var response = await OAuthTestClient.CreateClient(factory).GetAsync(OAuthTestClient.AuthorizeUrl(parameters), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task Metadata_document_with_mismatched_client_id_is_rejected()
    {
        var (factory, fetcher) = await OAuthTestClient.CreateFactoryAsync(postgres, Ct);
        await using var _f = factory;
        fetcher.Respond = _ => new ClientMetadata("https://claude.ai/someone-else", "Claude", [OAuthTestClient.ClaudeRedirectUri]);

        var response = await OAuthTestClient.CreateClient(factory)
            .GetAsync(OAuthTestClient.AuthorizeUrl(OAuthTestClient.AuthorizeParameters("challenge")), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("code_challenge_method", "plain", "invalid_request")]
    [InlineData("code_challenge", "", "invalid_request")]
    [InlineData("response_type", "token", "unsupported_response_type")]
    [InlineData("scope", "ledger:write", "invalid_scope")]
    [InlineData("resource", "https://evil.example/mcp", "invalid_target")]
    public async Task Invalid_parameters_redirect_back_with_error(string name, string value, string error)
    {
        var (factory, _) = await OAuthTestClient.CreateFactoryAsync(postgres, Ct);
        await using var _f = factory;
        var parameters = OAuthTestClient.AuthorizeParameters("challenge", state: "s1");
        parameters[name] = value;

        var response = await OAuthTestClient.CreateClient(factory).GetAsync(OAuthTestClient.AuthorizeUrl(parameters), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).Should().Be(OAuthTestClient.ClaudeRedirectUri);
        var query = QueryHelpers.ParseQuery(location.Query);
        query["error"].ToString().Should().Be(error);
        query["state"].ToString().Should().Be("s1");
    }

    [Fact]
    public async Task Loopback_redirect_with_any_port_is_accepted()
    {
        var (factory, _) = await OAuthTestClient.CreateFactoryAsync(postgres, Ct);
        await using var _f = factory;
        var parameters = OAuthTestClient.AuthorizeParameters("challenge");
        parameters["redirect_uri"] = "http://localhost:3118/callback";

        var response = await OAuthTestClient.CreateClient(factory).GetAsync(OAuthTestClient.AuthorizeUrl(parameters), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Consent_redirects_with_code_state_and_issuer()
    {
        var (factory, _) = await OAuthTestClient.CreateFactoryAsync(postgres, Ct);
        await using var _f = factory;

        var response = await OAuthTestClient.ConsentAsync(OAuthTestClient.CreateClient(factory), OAuthTestClient.AuthorizeParameters("challenge", "s2"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).Should().Be(OAuthTestClient.ClaudeRedirectUri);
        var query = QueryHelpers.ParseQuery(location.Query);
        query["code"].ToString().Should().NotBeNullOrEmpty();
        query["state"].ToString().Should().Be("s2");
        query["iss"].ToString().Should().Be("https://localhost");
    }

    [Fact]
    public async Task Deny_redirects_with_access_denied()
    {
        var (factory, _) = await OAuthTestClient.CreateFactoryAsync(postgres, Ct);
        await using var _f = factory;

        var response = await OAuthTestClient.ConsentAsync(OAuthTestClient.CreateClient(factory), OAuthTestClient.AuthorizeParameters("challenge"), Ct, decision: "deny");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var query = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
        query["error"].ToString().Should().Be("access_denied");
        query.ContainsKey("code").Should().BeFalse();
    }

    [Fact]
    public async Task Consent_without_antiforgery_token_is_rejected()
    {
        var (factory, _) = await OAuthTestClient.CreateFactoryAsync(postgres, Ct);
        await using var _f = factory;
        var form = OAuthTestClient.AuthorizeParameters("challenge");
        form["decision"] = "allow";

        var response = await OAuthTestClient.CreateClient(factory).PostAsync(OAuthEndpoints.AuthorizePath, new FormUrlEncodedContent(form!), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.Location.Should().BeNull();
    }
}
