using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SixJars.Application.Books;
using SixJars.Domain.Books;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

public class BookSettingsEndpointsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Add_account_returns_201_and_shows_up_in_book()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();

        var response = await client.PostAsJsonAsync($"/api/books/{book.Id.Value}/accounts",
            new { name = "郵局", type = "Bank", openingBalance = 123m }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().Be($"/api/books/{book.Id.Value}");
        var dto = await client.GetFromJsonAsync<BookDto>($"/api/books/{book.Id.Value}", ApiJson.Options, Ct);
        dto!.Accounts.Should().ContainSingle(a => a.Name == "郵局" && a.Type == AccountType.Bank && a.OpeningBalance == 123m);
    }

    [Fact]
    public async Task Blank_account_name_is_400_validation_problem()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await factory.CreateSignedInClient().PostAsJsonAsync($"/api/books/{book.Id.Value}/accounts",
            new { name = "", type = "Bank", openingBalance = 0m }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("errors").TryGetProperty("name", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Expense_main_category_without_nature_is_400()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await factory.CreateSignedInClient().PostAsJsonAsync($"/api/books/{book.Id.Value}/categories",
            new { name = "交通", kind = "Expense" }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("errors").TryGetProperty("nature", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Duplicate_account_name_is_422_with_rule_code()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await factory.CreateSignedInClient().PostAsJsonAsync($"/api/books/{book.Id.Value}/accounts",
            new { name = "現金", type = "Cash", openingBalance = 0m }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be("rule");
    }

    [Fact]
    public async Task Add_sub_category_under_main()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var food = book.FindCategory("主食")!;
        var client = factory.CreateSignedInClient();

        var response = await client.PostAsJsonAsync($"/api/books/{book.Id.Value}/categories",
            new { name = "晚餐", kind = "Expense", parentId = food.Id.Value }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await client.GetFromJsonAsync<BookDto>($"/api/books/{book.Id.Value}", ApiJson.Options, Ct);
        dto!.Categories.Should().ContainSingle(c => c.Name == "晚餐" && c.ParentId == food.Id.Value);
    }

    [Fact]
    public async Task Add_planning_fund()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();

        var response = await client.PostAsJsonAsync($"/api/books/{book.Id.Value}/planning-funds",
            new { name = "旅遊基金", openingBalance = 0m }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await client.GetFromJsonAsync<BookDto>($"/api/books/{book.Id.Value}", ApiJson.Options, Ct);
        dto!.PlanningFunds.Should().ContainSingle(f => f.Name == "旅遊基金");
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }
}
