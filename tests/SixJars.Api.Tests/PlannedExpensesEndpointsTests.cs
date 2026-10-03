using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SixJars.Application.Planning;
using SixJars.Domain.Books;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

public class PlannedExpensesEndpointsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_and_list_by_month()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();

        var response = await client.PostAsJsonAsync(Url(book), InsuranceInput(book, 202602, -3000m, "年繳"), ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<PlannedExpenseDto>(ApiJson.Options, Ct))!;
        var list = await ListAsync(client, book, "?budgetMonth=202602");
        var item = list.Should().ContainSingle().Subject;
        item.Should().BeEquivalentTo(created);
        item.BudgetMonth.Should().Be(202602);
        item.CategoryId.Should().Be(book.FindCategory("固定支出", "保險費")!.Id.Value);
        item.EstimatedAmount.Should().Be(-3000m);
        item.Note.Should().Be("年繳");
        item.IsPaid.Should().BeFalse();
        item.PaidTransactionId.Should().BeNull();
    }

    [Fact]
    public async Task List_by_month_excludes_other_months_and_other_books()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var mine = await factory.SeedBookAsync(Ct);
        var other = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        await CreateAsync(client, mine, InsuranceInput(mine, 202601, -1000m, "1 月"));
        var february = await CreateAsync(client, mine, InsuranceInput(mine, 202602, -2000m, "2 月"));
        await CreateAsync(client, mine, InsuranceInput(mine, 202603, -3000m, "3 月"));
        await CreateAsync(client, other, InsuranceInput(other, 202602, -9999m, "別人的 2 月"));

        var list = await ListAsync(client, mine, "?budgetMonth=202602");

        list.Select(p => p.Id).Should().Equal(february.Id);
    }

    [Fact]
    public async Task Create_with_floating_category_is_422()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await factory.CreateSignedInClient().PostAsJsonAsync(Url(book),
            new { budgetMonth = 202602, categoryId = book.FindCategory("主食", "午餐")!.Id.Value, estimatedAmount = -3000m },
            ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("rule");
    }

    [Fact]
    public async Task Update_with_stale_version_is_409()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var created = await CreateAsync(client, book, InsuranceInput(book, 202602, -3000m, "原本"));
        var url = $"{Url(book)}/{created.Id}";

        var first = await client.PutAsJsonAsync(url,
            new { version = created.Version, input = InsuranceInput(book, 202602, -3100m, "第一次修改") }, ApiJson.Options, Ct);
        var second = await client.PutAsJsonAsync(url,
            new { version = created.Version, input = InsuranceInput(book, 202602, -3200m, "第二次修改") }, ApiJson.Options, Ct);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await first.Content.ReadFromJsonAsync<PlannedExpenseDto>(ApiJson.Options, Ct))!;
        updated.EstimatedAmount.Should().Be(-3100m);
        updated.Version.Should().NotBe(created.Version);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var current = (await ListAsync(client, book, "")).Single();
        current.Note.Should().Be("第一次修改");
        current.Version.Should().Be(updated.Version);
    }

    [Fact]
    public async Task Update_planned_expense_of_another_book_is_404_and_leaves_it_unchanged()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var mine = await factory.SeedBookAsync(Ct);
        var other = await factory.SeedBookAsync(Ct);
        var client = factory.CreateSignedInClient();
        var theirs = await CreateAsync(client, other, InsuranceInput(other, 202602, -3000m, "別人的"));

        // 用自己帳本的路徑、自己帳本的分類，去改別本帳的預定支出：只用預定支出 Id 查詢就會改到。
        var response = await client.PutAsJsonAsync($"{Url(mine)}/{theirs.Id}",
            new { version = theirs.Version, input = InsuranceInput(mine, 202603, -1m, "被改掉了") }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ListAsync(client, other, "")).Should().ContainSingle().Which.Should().BeEquivalentTo(theirs);
    }

    internal static string Url(Book book) => $"/api/books/{book.Id.Value}/planned-expenses";

    internal static object InsuranceInput(Book book, int budgetMonth, decimal amount, string note) => new
    {
        budgetMonth, estimatedAmount = amount, note,
        categoryId = book.FindCategory("固定支出", "保險費")!.Id.Value, accountId = book.FindAccount("國泰世華銀行")!.Id.Value,
    };

    internal static async Task<PlannedExpenseDto> CreateAsync(HttpClient client, Book book, object input)
    {
        var response = await client.PostAsJsonAsync(Url(book), input, ApiJson.Options, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<PlannedExpenseDto>(ApiJson.Options, Ct))!;
    }

    internal static async Task<IReadOnlyList<PlannedExpenseDto>> ListAsync(HttpClient client, Book book, string query)
    {
        var response = await client.GetAsync($"{Url(book)}{query}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<PlannedExpenseDto>>(ApiJson.Options, Ct))!;
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }
}
