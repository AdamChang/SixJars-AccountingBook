using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SixJars.Application.Transactions;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

public class TransactionsEndpointsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_expense_returns_201_and_get_returns_postings()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var cash = book.FindAccount("現金")!.Id.Value;
        var lunch = book.FindCategory("主食", "午餐")!.Id.Value;
        var client = factory.CreateSignedInClient();

        var response = await client.PostAsJsonAsync($"/api/books/{book.Id.Value}/transactions",
            new { kind = "Expense", date = "2026-01-05", amount = -120m, accountId = cash, categoryId = lunch }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        created.TryGetProperty("version", out _).Should().BeTrue();
        var id = created.GetProperty("id").GetGuid();
        response.Headers.Location!.ToString().Should().Be($"/api/books/{book.Id.Value}/transactions/{id}");

        var dto = await client.GetFromJsonAsync<TransactionDto>($"/api/books/{book.Id.Value}/transactions/{id}", ApiJson.Options, Ct);
        dto!.Id.Should().Be(id);
        dto.Postings.Should().Equal(new PostingDto(cash, -120m));
        dto.BudgetMonth.Should().Be(202601);
    }

    [Fact]
    public async Task Transaction_of_another_book_is_404()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var mine = await factory.SeedBookAsync(Ct);
        var other = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var created = await client.PostAsJsonAsync($"/api/books/{other.Id.Value}/transactions",
            new
            {
                kind = "Expense", date = "2026-01-05", amount = -120m,
                accountId = other.FindAccount("現金")!.Id.Value, categoryId = other.FindCategory("主食", "午餐")!.Id.Value,
            },
            ApiJson.Options, Ct);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        // 用自己帳本的路徑讀別本帳的交易：只用交易 Id 查詢就會讀到（T21 審查的變異測試）
        var response = await client.GetAsync($"/api/books/{mine.Id.Value}/transactions/{id}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Unknown_transaction_is_404()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await factory.CreateSignedInClient().GetAsync($"/api/books/{book.Id.Value}/transactions/{Guid.NewGuid()}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Transfer_to_credit_card_is_422()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await factory.CreateSignedInClient().PostAsJsonAsync($"/api/books/{book.Id.Value}/transactions",
            new
            {
                kind = "Transfer",
                date = "2026-01-05",
                amount = 500m,
                accountId = book.FindAccount("國泰世華銀行")!.Id.Value,
                counterAccountId = book.FindAccount("國泰Combo卡")!.Id.Value,
            }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be("rule");
    }

    [Fact]
    public async Task Transfer_without_counter_account_is_400()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await factory.CreateSignedInClient().PostAsJsonAsync($"/api/books/{book.Id.Value}/transactions",
            new { kind = "Transfer", date = "2026-01-05", amount = 500m, accountId = book.FindAccount("國泰世華銀行")!.Id.Value },
            ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("errors").TryGetProperty("Input.CounterAccountId", out _).Should().BeTrue();
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }
}
