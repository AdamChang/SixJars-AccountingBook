using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SixJars.Application.Transactions;
using SixJars.Domain.Books;
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

    [Fact]
    public async Task List_is_ordered_by_date_then_creation()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var tenth = await CreateLunchAsync(client, book, "2026-01-10", "1/10");
        var fifth = await CreateLunchAsync(client, book, "2026-01-05", "先建立");
        var fifthLater = await CreateLunchAsync(client, book, "2026-01-05", "後建立");

        var list = await ListAsync(client, book, "");

        list.Select(t => t.Id).Should().Equal(fifth, fifthLater, tenth);
    }

    [Fact]
    public async Task List_filters_by_budget_month()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        await CreateLunchAsync(client, book, "2026-01-15", "1 月");
        var salary = await CreateAsync(client, book, new
        {
            kind = "Income", date = "2026-01-30", budgetMonth = 202602, amount = 50000m,
            accountId = book.FindAccount("國泰世華銀行")!.Id.Value, categoryId = book.FindCategory("工作薪資")!.Id.Value,
        });
        var february = await CreateLunchAsync(client, book, "2026-02-03", "2 月");

        var list = await ListAsync(client, book, "?budgetMonth=202602");

        list.Select(t => t.Id).Should().Equal(salary, february);
    }

    [Fact]
    public async Task List_filters_by_account_including_counter_account()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var cash = book.FindAccount("現金")!.Id.Value;
        var bank = book.FindAccount("國泰世華銀行")!.Id.Value;
        var expense = await CreateLunchAsync(client, book, "2026-01-05", "現金午餐");
        var transfer = await CreateAsync(client, book,
            new { kind = "Transfer", date = "2026-01-06", amount = 500m, accountId = bank, counterAccountId = cash });
        await CreateAsync(client, book, new
        {
            kind = "Expense", date = "2026-01-07", amount = -80m,
            accountId = bank, categoryId = book.FindCategory("主食", "午餐")!.Id.Value,
        });

        var list = await ListAsync(client, book, $"?accountId={cash}");

        list.Select(t => t.Id).Should().Equal(expense, transfer);
    }

    [Fact]
    public async Task List_by_account_of_another_book_returns_nothing()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var mine = await factory.SeedBookAsync(Ct);
        var other = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        await CreateLunchAsync(client, mine, "2026-01-05", "我的");
        await CreateLunchAsync(client, other, "2026-01-05", "別人的");

        // 用自己帳本的路徑、帶別本帳的帳戶 Id 篩選：只用帳戶條件查詢就會讀到別本帳的交易。
        var list = await ListAsync(client, mine, $"?accountId={other.FindAccount("現金")!.Id.Value}");

        list.Should().BeEmpty();
    }

    private static Task<Guid> CreateLunchAsync(HttpClient client, Book book, string date, string note) =>
        CreateAsync(client, book, new
        {
            kind = "Expense", date, amount = -120m, note,
            accountId = book.FindAccount("現金")!.Id.Value, categoryId = book.FindCategory("主食", "午餐")!.Id.Value,
        });

    private static async Task<Guid> CreateAsync(HttpClient client, Book book, object input)
    {
        var response = await client.PostAsJsonAsync($"/api/books/{book.Id.Value}/transactions", input, ApiJson.Options, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private static async Task<IReadOnlyList<TransactionDto>> ListAsync(HttpClient client, Book book, string query)
    {
        var response = await client.GetAsync($"/api/books/{book.Id.Value}/transactions{query}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<TransactionDto>>(ApiJson.Options, Ct))!;
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }
}
