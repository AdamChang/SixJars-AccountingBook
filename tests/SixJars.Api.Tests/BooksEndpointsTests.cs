using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using SixJars.Application.Books;
using SixJars.Domain.Books;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

public class BooksEndpointsTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Get_book_returns_accounts_funds_and_categories()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ApiFactory.CreateAsync(postgres, ct);
        var book = await factory.SeedBookAsync(ct);

        var dto = await (await factory.CreateMemberClientAsync()).GetFromJsonAsync<BookDto>($"/api/books/{book.Id.Value}", ApiJson.Options, ct);

        dto!.Name.Should().Be("測試帳本");
        dto.Accounts.Should().ContainSingle(a => a.Name == "悠遊卡" && a.Type == AccountType.EWallet);
        dto.PlanningFunds.Should().ContainSingle(f => f.Name == "財務自由帳戶" && f.OpeningBalance == 10000m);
        dto.Categories.Should().ContainSingle(c => c.Name == "午餐" && c.ParentId != null);
        dto.LockDate.Should().BeNull();
    }

    [Fact]
    public async Task Unknown_book_is_404_problem_details()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = await ApiFactory.CreateAsync(postgres, ct);

        var response = await (await factory.CreateMemberClientAsync()).GetAsync($"/api/books/{Guid.NewGuid()}", ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }
}
