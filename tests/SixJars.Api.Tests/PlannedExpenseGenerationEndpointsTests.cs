using System.Net;
using System.Net.Http.Json;
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
using static SixJars.Api.Tests.RecurringPlannedExpensesEndpointsTests;

namespace SixJars.Api.Tests;

public class PlannedExpenseGenerationEndpointsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Generate_creates_due_items_and_second_call_reports_already_generated()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var january = await CreateAsync(client, book, Insurance(book));                               // 1、7 月
        await CreateAsync(client, book, Insurance(book) with { Months = [3] });                        // 1 月不適用

        var first = await GenerateAsync(client, book, 202601);
        var second = await GenerateAsync(client, book, 202601);

        first.Created.Should().ContainSingle().Which.EstimatedAmount.Should().Be(-3000m);
        first.Skipped.Should().BeEmpty();
        second.Created.Should().BeEmpty();
        second.Skipped.Should().Equal(new RecurringSkipDto(january.Id, null, RecurringSkipReason.AlreadyGenerated));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
        (await db.PlannedExpenses.SingleAsync(Ct)).SourceId.Should().Be(new RecurringPlannedExpenseId(january.Id));
    }

    [Fact]
    public async Task Deleted_generated_item_is_not_recreated()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var item = await CreateAsync(client, book, Insurance(book));
        var created = (await GenerateAsync(client, book, 202601)).Created.Single();
        await client.DeleteAsync($"/api/books/{book.Id.Value}/planned-expenses/{created.Id}?version={created.Version}", Ct);

        var again = await GenerateAsync(client, book, 202601);

        again.Created.Should().BeEmpty();
        again.Skipped.Single().Reason.Should().Be(RecurringSkipReason.AlreadyGenerated);
    }

    [Fact]
    public async Task Archived_category_is_skipped_with_reason()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var item = await CreateAsync(client, book, Insurance(book));
        var fixedMain = book.FindCategory("固定支出")!.Id.Value;
        (await client.PostAsync($"/api/books/{book.Id.Value}/categories/{fixedMain}/archive", null, Ct)).EnsureSuccessStatusCode();

        var result = await GenerateAsync(client, book, 202601);

        result.Skipped.Should().Equal(new RecurringSkipDto(item.Id, null, RecurringSkipReason.CategoryArchived));
    }

    [Fact]
    public async Task Locked_month_is_422_locked()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        await CreateAsync(client, book, Insurance(book));
        (await client.PutAsJsonAsync($"/api/books/{book.Id.Value}/lock-date", new { lockDate = "2026-01-31" }, ApiJson.Options, Ct))
            .EnsureSuccessStatusCode();

        var response = await client.PostAsync($"/api/books/{book.Id.Value}/planned-expenses/generate?budgetMonth=202601", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SettingsMaintenanceEndpointsTests.ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("locked");
    }

    [Theory]
    [InlineData(202613)]
    [InlineData(1)]
    [InlineData(189912)]
    [InlineData(300001)]
    [InlineData(100000001)]
    public async Task Invalid_month_is_400_and_generates_nothing(int budgetMonth)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        // 有一筆週期項目，確認壞月份不會被當成「到期」而產生預定支出。
        await CreateAsync(client, book, Insurance(book) with { StartMonth = 190001 });

        var response = await client.PostAsync($"/api/books/{book.Id.Value}/planned-expenses/generate?budgetMonth={budgetMonth}", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SettingsMaintenanceEndpointsTests.ReadProblemAsync(response)).GetProperty("errors").TryGetProperty("budgetMonth", out _).Should().BeTrue();
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<SixJarsDbContext>().PlannedExpenses.CountAsync(Ct)).Should().Be(0);
    }

    [Theory]
    [InlineData(190001)]
    [InlineData(299912)]
    public async Task Boundary_years_are_accepted(int budgetMonth)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        // 週期項目 1900-01 起、每月適用，兩個邊界月份都到期，才分辨得出「有產生」與「被擋下」。
        await CreateAsync(client, book, Insurance(book) with { Frequency = RecurrenceFrequency.Monthly, Months = [], StartMonth = 190001 });

        var result = await GenerateAsync(client, book, budgetMonth);

        result.Created.Should().ContainSingle().Which.BudgetMonth.Should().Be(budgetMonth);
        result.Skipped.Should().BeEmpty();
    }

    [Fact]
    public async Task Refresh_applies_current_values_to_unpaid_generated_items_only()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var item = await CreateAsync(client, book, Insurance(book) with { Frequency = RecurrenceFrequency.Monthly, Months = [] });
        await GenerateAsync(client, book, 202601);
        var paid = (await GenerateAsync(client, book, 202602)).Created.Single();
        await PayAsync(client, book, paid);
        await client.PutAsJsonAsync(RecurringUrl(book, item.Id),
            new { version = item.Version, input = Insurance(book) with { Frequency = RecurrenceFrequency.Monthly, Months = [], DefaultAmount = -3600m } },
            ApiJson.Options, Ct);

        var january = await RefreshAsync(client, book, 202601);
        var february = await RefreshAsync(client, book, 202602);

        january.Updated.Should().ContainSingle().Which.EstimatedAmount.Should().Be(-3600m);
        february.Updated.Should().BeEmpty();   // 已付款的不更新
    }

    [Fact]
    public async Task Refresh_reports_not_due_and_leaves_item_unchanged()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var item = await CreateAsync(client, book, Insurance(book));                  // 1、7 月
        var generated = (await GenerateAsync(client, book, 202601)).Created.Single();
        await client.PutAsJsonAsync(RecurringUrl(book, item.Id),
            new { version = item.Version, input = Insurance(book) with { Months = [7], DefaultAmount = -1m } }, ApiJson.Options, Ct);

        var result = await RefreshAsync(client, book, 202601);

        result.Updated.Should().BeEmpty();
        result.Skipped.Should().Equal(new RecurringSkipDto(item.Id, generated.Id, RecurringSkipReason.NotDue));
    }

    [Fact]
    public async Task Refresh_writes_one_update_audit_per_changed_item()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var item = await CreateAsync(client, book, Insurance(book));
        var generated = (await GenerateAsync(client, book, 202601)).Created.Single();
        await client.PutAsJsonAsync(RecurringUrl(book, item.Id),
            new { version = item.Version, input = Insurance(book) with { Note = "改備註" } }, ApiJson.Options, Ct);

        await RefreshAsync(client, book, 202601);
        await RefreshAsync(client, book, 202601);   // 第二次沒有改變，不寫稽核（D8）

        var history = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(
            $"/api/books/{book.Id.Value}/audit?entityId={generated.Id}", ApiJson.Options, Ct);
        history.EnumerateArray().Select(e => e.GetProperty("action").GetString()).Should().BeEquivalentTo("Create", "Update");
    }

    [Fact]
    public async Task Refresh_on_locked_month_is_422_locked()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        (await client.PutAsJsonAsync($"/api/books/{book.Id.Value}/lock-date", new { lockDate = "2026-01-31" }, ApiJson.Options, Ct))
            .EnsureSuccessStatusCode();

        var response = await client.PostAsync($"/api/books/{book.Id.Value}/planned-expenses/refresh?budgetMonth=202601", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Theory]
    [InlineData(202613)]
    [InlineData(1)]
    [InlineData(189912)]
    [InlineData(300001)]
    [InlineData(100000001)]
    public async Task Refresh_with_invalid_month_is_400_and_changes_nothing(int budgetMonth)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var item = await CreateAsync(client, book, Insurance(book) with { Frequency = RecurrenceFrequency.Monthly, Months = [], StartMonth = 190001 });
        await GenerateAsync(client, book, 202601);
        (await client.PutAsJsonAsync(RecurringUrl(book, item.Id),
            new { version = item.Version, input = Insurance(book) with { Frequency = RecurrenceFrequency.Monthly, Months = [], StartMonth = 190001, DefaultAmount = -3600m } },
            ApiJson.Options, Ct)).EnsureSuccessStatusCode();

        var response = await client.PostAsync($"/api/books/{book.Id.Value}/planned-expenses/refresh?budgetMonth={budgetMonth}", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SettingsMaintenanceEndpointsTests.ReadProblemAsync(response)).GetProperty("errors").TryGetProperty("budgetMonth", out _).Should().BeTrue();
        // 週期項目已改成 -3600；若壞月份被放行並誤更新了既有項目，這裡就不會是原本的 -3000。
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<SixJarsDbContext>().PlannedExpenses.SingleAsync(Ct)).EstimatedAmount.Should().Be(-3000m);
    }

    [Theory]
    [InlineData(190001)]
    [InlineData(299912)]
    public async Task Refresh_boundary_years_are_accepted_and_applied(int budgetMonth)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var item = await CreateAsync(client, book, Insurance(book) with { Frequency = RecurrenceFrequency.Monthly, Months = [], StartMonth = 190001 });
        await GenerateAsync(client, book, budgetMonth);
        (await client.PutAsJsonAsync(RecurringUrl(book, item.Id),
            new { version = item.Version, input = Insurance(book) with { Frequency = RecurrenceFrequency.Monthly, Months = [], StartMonth = 190001, DefaultAmount = -3600m } },
            ApiJson.Options, Ct)).EnsureSuccessStatusCode();

        var result = await RefreshAsync(client, book, budgetMonth);

        result.Updated.Should().ContainSingle().Which.EstimatedAmount.Should().Be(-3600m);
    }

    private static string RecurringUrl(Book book, Guid id) => $"/api/books/{book.Id.Value}/recurring-planned-expenses/{id}";

    private static async Task<RefreshPlannedExpensesResult> RefreshAsync(HttpClient client, Book book, int month)
    {
        var response = await client.PostAsync($"/api/books/{book.Id.Value}/planned-expenses/refresh?budgetMonth={month}", null, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<RefreshPlannedExpensesResult>(ApiJson.Options, Ct))!;
    }

    private static async Task PayAsync(HttpClient client, Book book, PlannedExpenseDto planned)
    {
        var response = await client.PostAsJsonAsync($"/api/books/{book.Id.Value}/planned-expenses/{planned.Id}/pay",
            new { version = planned.Version, date = "2026-02-05", accountId = book.FindAccount("國泰世華銀行")!.Id.Value, amount = -3000m },
            ApiJson.Options, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    internal static async Task<GeneratePlannedExpensesResult> GenerateAsync(HttpClient client, Book book, int month)
    {
        var response = await client.PostAsync($"/api/books/{book.Id.Value}/planned-expenses/generate?budgetMonth={month}", null, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<GeneratePlannedExpensesResult>(ApiJson.Options, Ct))!;
    }
}
