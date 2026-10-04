using System.Net;
using FluentAssertions;
using SixJars.Api.Infrastructure;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests.Auth;

/// <summary><c>/auth/**</c>：不在 <c>/api</c> 底下，未登入也能呼叫。</summary>
public class AuthEndpointsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Old_denied_endpoint_is_gone()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);

        var response = await factory.CreateClient().GetAsync("/auth/denied", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Logout_clears_the_sign_in_cookie()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);

        var response = await (await factory.CreateMemberClientAsync()).PostAsync("/auth/logout", content: null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Set-Cookie").Should().Contain(c =>
            c.StartsWith($"{AuthenticationSetup.CookieName}=;", StringComparison.Ordinal) && c.Contains("expires=Thu, 01 Jan 1970"));
    }
}
