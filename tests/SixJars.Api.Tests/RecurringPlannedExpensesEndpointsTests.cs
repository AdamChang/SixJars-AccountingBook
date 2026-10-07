using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Application.Planning;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

public class RecurringPlannedExpensesEndpointsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_then_list_returns_the_item()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();

        var response = await client.PostAsJsonAsync(Url(book), Insurance(book), ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await response.Content.ReadFromJsonAsync<RecurringPlannedExpenseDto>(ApiJson.Options, Ct))!;
        var list = (await client.GetFromJsonAsync<List<RecurringPlannedExpenseDto>>(Url(book), ApiJson.Options, Ct))!;
        list.Should().ContainSingle().Which.Should().BeEquivalentTo(created);
        created.Frequency.Should().Be(RecurrenceFrequency.Yearly);
        created.Months.Should().Equal(1, 7);
        (created.StartMonth, created.EndMonth).Should().Be((202601, (int?)null));
    }

    [Fact]
    public async Task Floating_category_is_422_and_positive_amount_is_400()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();

        var floating = await client.PostAsJsonAsync(Url(book),
            Insurance(book) with { CategoryId = book.FindCategory("主食", "午餐")!.Id.Value }, ApiJson.Options, Ct);
        var positive = await client.PostAsJsonAsync(Url(book), Insurance(book) with { DefaultAmount = 3000m }, ApiJson.Options, Ct);

        floating.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        positive.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_with_current_version_changes_the_item_and_writes_audit()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var created = await CreateAsync(client, book, Insurance(book));

        var response = await client.PutAsJsonAsync(Url(book, $"/{created.Id}"),
            new { version = created.Version, input = Insurance(book) with { DefaultAmount = -3500m, EndMonth = 202612 } }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<RecurringPlannedExpenseDto>(ApiJson.Options, Ct))!;
        (updated.DefaultAmount, updated.EndMonth).Should().Be((-3500m, (int?)202612));
        var history = await client.GetFromJsonAsync<JsonElement>(
            $"/api/books/{book.Id.Value}/audit?entityId={created.Id}", ApiJson.Options, Ct);
        history.EnumerateArray().Select(e => e.GetProperty("action").GetString()).Should().BeEquivalentTo("Create", "Update");
        history.EnumerateArray().Should().OnlyContain(e => e.GetProperty("entityType").GetString() == "RecurringPlannedExpense");
    }

    [Fact]
    public async Task Update_with_stale_version_is_409()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var created = await CreateAsync(client, book, Insurance(book));
        await client.PutAsJsonAsync(Url(book, $"/{created.Id}"), new { version = created.Version, input = Insurance(book) }, ApiJson.Options, Ct);

        var stale = await client.PutAsJsonAsync(Url(book, $"/{created.Id}"), new { version = created.Version, input = Insurance(book) }, ApiJson.Options, Ct);

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Other_books_item_is_404()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var mine = await factory.SeedBookAsync(Ct);
        var other = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var theirs = await CreateAsync(client, other, Insurance(other));

        var response = await client.PutAsJsonAsync(Url(mine, $"/{theirs.Id}"), new { version = theirs.Version, input = Insurance(mine) }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_unused_item_is_204_and_writes_audit()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var created = await CreateAsync(client, book, Insurance(book));

        var response = await client.DeleteAsync(Url(book, $"/{created.Id}?version={created.Version}"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<List<RecurringPlannedExpenseDto>>(Url(book), ApiJson.Options, Ct)).Should().BeEmpty();
        var history = await client.GetFromJsonAsync<JsonElement>(
            $"/api/books/{book.Id.Value}/audit?entityId={created.Id}", ApiJson.Options, Ct);
        history.EnumerateArray().Select(e => e.GetProperty("action").GetString()).Should().Contain("Delete");
    }

    [Fact]
    public async Task Delete_is_422_in_use_even_when_the_generated_planned_expense_was_deleted()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var created = await CreateAsync(client, book, Insurance(book));
        await factory.SeedGeneratedPlannedExpenseAsync(book, created.Id, 202601, deleted: true, Ct);

        var response = await client.DeleteAsync(Url(book, $"/{created.Id}?version={created.Version}"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SettingsMaintenanceEndpointsTests.ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("in-use");
    }

    internal static RecurringPlannedExpenseInput Insurance(Book book) => new(
        book.FindCategory("固定支出", "保險費")!.Id.Value, null, -3000m, "保險", RecurrenceFrequency.Yearly, [7, 1], 202601, null);

    internal static async Task<RecurringPlannedExpenseDto> CreateAsync(HttpClient client, Book book, RecurringPlannedExpenseInput input)
    {
        var response = await client.PostAsJsonAsync(Url(book), input, ApiJson.Options, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<RecurringPlannedExpenseDto>(ApiJson.Options, Ct))!;
    }

    internal static string Url(Book book, string suffix = "") => $"/api/books/{book.Id.Value}/recurring-planned-expenses{suffix}";
}

internal static class RecurringSeed
{
    /// <summary>直接寫入一筆由週期項目產生的預定支出（繞過 generate API）。</summary>
    public static async Task<Guid> SeedGeneratedPlannedExpenseAsync(
        this ApiFactory factory, Book book, Guid recurringId, int budgetMonth, bool deleted, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
        var source = await db.RecurringPlannedExpenses.SingleAsync(r => r.Id == new RecurringPlannedExpenseId(recurringId), ct);
        var planned = PlannedExpense.Create(book, BudgetMonth.FromKey(budgetMonth), source.CategoryId, source.AccountId,
            source.DefaultAmount, source.Note, sourceId: source.Id);
        if (deleted)
        {
            planned.Delete(DateTimeOffset.UtcNow);
        }

        db.PlannedExpenses.Add(planned);
        await db.SaveChangesAsync(ct);
        return planned.Id.Value;
    }
}
