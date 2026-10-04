using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

public class SpaHostingTests(PostgresFixture postgres)
{
    private static readonly WebApplicationFactoryClientOptions ClientOptions = new() { BaseAddress = new Uri("https://localhost") };

    private async Task<ApiFactory> CreateFactoryWithWebRootAsync(CancellationToken ct) =>
        new(await postgres.CreateConnectionStringAsync(ct), settings: new Dictionary<string, string> { ["webroot"] = SpaWebRoot.Create() });

    [Theory]
    [InlineData("/")]
    [InlineData("/books/7b0c2c1e-0000-0000-0000-000000000001/transactions?month=202603")]
    [InlineData("/denied")]
    public async Task Frontend_routes_serve_index_html(string url)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryWithWebRootAsync(ct);

        var response = await factory.CreateClient(ClientOptions).GetAsync(url, ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync(ct)).Should().Contain(SpaWebRoot.IndexMarker);
    }

    [Theory]
    [InlineData("/api/nope")]
    [InlineData("/api")]
    [InlineData("/auth/nope")]
    public async Task Unknown_api_and_auth_paths_are_404_not_html(string url)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryWithWebRootAsync(ct);

        var response = await factory.CreateClient(ClientOptions).GetAsync(url, ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().NotBe("text/html");
        (await response.Content.ReadAsStringAsync(ct)).Should().NotContain(SpaWebRoot.IndexMarker);
    }

    [Fact]
    public async Task Missing_file_with_extension_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryWithWebRootAsync(ct);

        var response = await factory.CreateClient(ClientOptions).GetAsync("/main-OLD12345.js", ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Existing_static_file_is_served()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryWithWebRootAsync(ct);

        var response = await factory.CreateClient(ClientOptions).GetAsync("/main-ABCD1234.js", ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/javascript");
    }

    [Fact]
    public async Task Post_to_frontend_route_is_not_rewritten()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryWithWebRootAsync(ct);

        var response = await factory.CreateClient(ClientOptions).PostAsync("/books/x/transactions", new StringContent(""), ct);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        (await response.Content.ReadAsStringAsync(ct)).Should().NotContain(SpaWebRoot.IndexMarker);
    }

    [Fact]
    public async Task Without_web_root_frontend_paths_are_404_and_health_still_works()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ApiFactory.CreateAsync(postgres, ct);
        var client = factory.CreateClient(ClientOptions);

        (await client.GetAsync("/books/x", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/health", ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
