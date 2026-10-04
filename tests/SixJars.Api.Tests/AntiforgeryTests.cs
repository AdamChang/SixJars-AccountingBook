using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using SixJars.Application.Books;
using SixJars.Domain.Books;
using SixJars.Tests.Shared;
using Xunit;
using SameSiteMode = Microsoft.Net.Http.Headers.SameSiteMode;

namespace SixJars.Api.Tests;

/// <summary>
/// ADR 0005：登入靠同源 cookie，所以非 GET 的請求一律要帶 <c>X-XSRF-TOKEN</c> header（值取自 <c>XSRF-TOKEN</c> cookie），
/// 而且 token 與取得時的登入身分綁定。
/// </summary>
public class AntiforgeryTests(PostgresFixture postgres)
{
    private const string InvalidTokenTitle = "缺少或無效的 XSRF token";
    private const string SpouseSubject = "spouse-sub";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Post_without_token_is_400()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await AddAccountAsync(factory.CreateSignedInClient(), book);

        await ShouldBeInvalidTokenAsync(response);
        (await AccountNamesAsync(factory, book)).Should().NotContain("郵局", "驗證失敗時 handler 不能執行");
    }

    [Fact]
    public async Task Post_with_token_succeeds()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await AddAccountAsync(await factory.CreateMemberClientAsync(), book);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>兩人都是擁有者，只差在 token 是誰取得的；用自己的 token 就會成功（見 <see cref="Post_with_token_succeeds"/>）。</summary>
    [Fact]
    public async Task Token_of_another_user_is_400()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        await factory.AddOwnerAsync(book.Id, SpouseSubject, Ct);
        var ownerTokens = await ApiXsrfTokens.FetchAsync(factory.CreateSignedInClient());
        var spouse = factory.CreateSignedInClient(SpouseSubject);
        ownerTokens.ApplyTo(spouse);

        var response = await AddAccountAsync(spouse, book);

        await ShouldBeInvalidTokenAsync(response);
    }

    /// <summary>登入前取得的 token（匿名身分）在登入後不能沿用，避免攻擊者先塞給受害者一組自己知道的 token。</summary>
    [Fact]
    public async Task Token_issued_before_sign_in_is_400()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var anonymous = new DefaultHttpContext { RequestServices = factory.Services };
        anonymous.Request.Scheme = "https";
        var tokens = factory.Services.GetRequiredService<IAntiforgery>().GetTokens(anonymous);
        var cookieName = factory.Services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.Cookie.Name;
        var client = factory.CreateSignedInClient();
        client.DefaultRequestHeaders.Add(HeaderNames.Cookie, $"{cookieName}={tokens.CookieToken}");
        client.DefaultRequestHeaders.Add(ApiXsrfTokens.HeaderName, tokens.RequestToken);

        var response = await AddAccountAsync(client, book);

        await ShouldBeInvalidTokenAsync(response);
    }

    [Fact]
    public async Task Get_does_not_require_token()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await factory.CreateSignedInClient().GetAsync($"/api/books/{book.Id.Value}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>不在 <c>/api</c> 底下，但別的網站可以用 form POST 強迫使用者登出，所以一樣要驗證。</summary>
    [Fact]
    public async Task Logout_without_token_is_400()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);

        var response = await factory.CreateSignedInClient().PostAsync("/auth/logout", content: null, Ct);

        await ShouldBeInvalidTokenAsync(response);
        response.Headers.Contains(HeaderNames.SetCookie).Should().BeFalse("驗證失敗時不能清除登入 cookie");
    }

    /// <summary>
    /// <c>XSRF-TOKEN</c> 要讓前端 JavaScript 讀得到（不能 HttpOnly）；antiforgery 自己的 cookie token 則不需要，一律 HttpOnly。
    /// 兩者都只走 HTTPS、SameSite=Strict。
    /// </summary>
    [Fact]
    public async Task Token_cookies_have_secure_attributes()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);

        var tokens = await ApiXsrfTokens.FetchAsync(factory.CreateSignedInClient());

        tokens.Cookies.Should().HaveCount(2);
        var requestToken = tokens.Cookies.Single(c => c.Name == ApiXsrfTokens.RequestTokenCookie);
        requestToken.HttpOnly.Should().BeFalse();
        requestToken.Secure.Should().BeTrue();
        requestToken.SameSite.Should().Be(SameSiteMode.Strict);
        requestToken.Path.ToString().Should().Be("/");
        var cookieToken = tokens.Cookies.Single(c => c.Name != ApiXsrfTokens.RequestTokenCookie);
        cookieToken.Name.ToString().Should().Be("__Host-sixjars-af", "__Host- 前綴與登入 cookie 一致，也不透露後端技術");
        cookieToken.HttpOnly.Should().BeTrue();
        cookieToken.Secure.Should().BeTrue();
        cookieToken.SameSite.Should().Be(SameSiteMode.Strict);
        cookieToken.Path.ToString().Should().Be("/");
    }

    private static Task<HttpResponseMessage> AddAccountAsync(HttpClient client, Book book) =>
        client.PostAsJsonAsync($"/api/books/{book.Id.Value}/accounts",
            new { name = "郵局", type = "Bank", openingBalance = 0m }, ApiJson.Options, Ct);

    private static async Task<IEnumerable<string>> AccountNamesAsync(ApiFactory factory, Book book)
    {
        var client = factory.CreateSignedInClient();
        var dto = await client.GetFromJsonAsync<BookDto>($"/api/books/{book.Id.Value}", ApiJson.Options, Ct);
        return dto!.Accounts.Select(a => a.Name);
    }

    /// <summary>要確認是 antiforgery 的 400，而不是綁定或 validation 的 400；也不能是未處理的例外。</summary>
    internal static async Task ShouldBeInvalidTokenAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        problem!.Title.Should().Be(InvalidTokenTitle);
    }
}
