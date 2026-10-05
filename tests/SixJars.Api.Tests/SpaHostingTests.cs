using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using SixJars.Api.Infrastructure;
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
    [InlineData("/API/nope")]
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

        var response = await factory.CreateClient(ClientOptions).GetAsync("/main-ABCD2345.js", ct);

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

    [Theory]
    [InlineData("/")]
    [InlineData("/books/x/transactions")]
    [InlineData("/index.html")]
    public async Task Index_is_no_cache_for_root_and_deep_link(string url)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryWithWebRootAsync(ct);

        var response = await factory.CreateClient(ClientOptions).GetAsync(url, ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoCache.Should().BeTrue();
    }

    [Fact]
    public async Task Hashed_assets_are_immutable()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryWithWebRootAsync(ct);

        var response = await factory.CreateClient(ClientOptions).GetAsync("/main-ABCD2345.js", ct);

        var cacheControl = response.Headers.CacheControl!;
        cacheControl.Public.Should().BeTrue();
        cacheControl.MaxAge.Should().Be(TimeSpan.FromDays(365));
        cacheControl.ToString().Should().Contain("immutable");
    }

    [Theory]
    [InlineData("/ngsw.json")]
    public async Task Unhashed_files_are_no_cache(string url)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryWithWebRootAsync(ct);

        var response = await factory.CreateClient(ClientOptions).GetAsync(url, ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoCache.Should().BeTrue();
        response.Headers.CacheControl.MaxAge.Should().BeNull();
    }

    [Theory]
    [InlineData("main-ABCD2345.js", true)]
    [InlineData("chunk-Z7Y6X5W4.js", true)]
    [InlineData("styles-ABCDEFGH.css", true)]
    [InlineData("ngsw.json", false)]
    [InlineData("ngsw-worker.js", false)]
    [InlineData("main-abcd2345.js", false)]
    [InlineData("favicon.ico", false)]
    [InlineData("logo-20261004.png", false)]
    [InlineData("chunk-BvxS2djg.js", true)]
    [InlineData("chunk-CmnSPI3X.js", true)]
    [InlineData("chunk-a_b-CdEf.js", true)]
    [InlineData("chunk-short.js", false)]
    [InlineData("chunk-BvxS2djg.css", false)]
    [InlineData("vendor-BvxS2djg.js", false)]
    public void IsHashedAsset_matches_only_build_hashes(string fileName, bool expected) =>
        SpaHosting.IsHashedAsset(fileName).Should().Be(expected);
}
