using System.Net;
using FluentAssertions;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

public class HealthEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Health_checks_the_database_without_signing_in()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ApiFactory.CreateAsync(postgres, ct);

        var response = await factory.CreateClient().GetAsync("/health", ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void Startup_fails_without_a_connection_string()
    {
        // 前提：執行測試的環境沒有設定 ConnectionStrings__SixJars
        using var factory = new ApiFactory(connectionString: null);

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>().WithMessage("*ConnectionStrings__SixJars*");
    }
}
