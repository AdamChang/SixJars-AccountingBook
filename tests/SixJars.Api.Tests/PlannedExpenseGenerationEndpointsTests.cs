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

    [Fact]
    public async Task Invalid_month_is_400()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await (await factory.CreateMemberClientAsync())
            .PostAsync($"/api/books/{book.Id.Value}/planned-expenses/generate?budgetMonth=202613", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    internal static async Task<GeneratePlannedExpensesResult> GenerateAsync(HttpClient client, Book book, int month)
    {
        var response = await client.PostAsync($"/api/books/{book.Id.Value}/planned-expenses/generate?budgetMonth={month}", null, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<GeneratePlannedExpensesResult>(ApiJson.Options, Ct))!;
    }
}
