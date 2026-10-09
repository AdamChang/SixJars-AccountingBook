using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Application.Planning;
using SixJars.Domain.Books;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

/// <summary>預算（P4 段 L）：寫入（L5）與查詢（L6）。</summary>
public class BudgetsEndpointsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task First_default_creates_the_budget_and_audits_create()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var food = Food(book);

        var response = await client.PutAsJsonAsync(Url(book, $"/{food}/default"), new { amount = 5000m }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = (await response.Content.ReadFromJsonAsync<CategoryBudgetDto>(ApiJson.Options, Ct))!;
        (dto.CategoryId, dto.DefaultAmount).Should().Be((food, (decimal?)5000m));
        dto.Overrides.Should().BeEmpty();
        var history = await AuditAsync(client, book, food);
        history.Should().ContainSingle();
        history[0].GetProperty("action").GetString().Should().Be("Create");
        history[0].GetProperty("entityType").GetString().Should().Be("CategoryBudget");
        IsNull(history[0], "before").Should().BeTrue();
    }

    [Fact]
    public async Task Removing_default_keeps_overrides_and_every_write_is_audited()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var food = Food(book);
        await SetDefaultAsync(client, book, food, 5000m);
        await SetOverrideAsync(client, book, food, 202603, 3000m);
        await SetOverrideAsync(client, book, food, 202603, 3000m);   // 同值照樣寫稽核（Q8b）

        var removed = await client.DeleteAsync(Url(book, $"/{food}/default"), Ct);
        var dto = await SetOverrideAsync(client, book, food, 202601, 100m);

        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        dto.DefaultAmount.Should().BeNull();
        // 依月份排序（Q7），不是寫入順序
        dto.Overrides.Should().Equal(new BudgetOverrideDto(202601, 100m), new BudgetOverrideDto(202603, 3000m));
        (await AuditAsync(client, book, food)).Select(e => e.GetProperty("action").GetString())
            .Should().BeEquivalentTo("Create", "Update", "Update", "Update", "Update");
    }

    [Fact]
    public async Task Clearing_the_last_value_deletes_the_budget_and_audits_delete()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var food = Food(book);
        await SetOverrideAsync(client, book, food, 202601, 1000m);

        var response = await client.DeleteAsync(Url(book, $"/{food}/overrides/202601"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<SixJarsDbContext>().CategoryBudgets.CountAsync(Ct)).Should().Be(0);
        }

        var deleted = (await AuditAsync(client, book, food)).Single(e => e.GetProperty("action").GetString() == "Delete");
        IsNull(deleted, "after").Should().BeTrue();
        // 刪除後可以重新建立，稽核歷史仍掛在同一個分類 Id 底下（Q6）
        await SetDefaultAsync(client, book, food, 200m);
        (await AuditAsync(client, book, food)).Select(e => e.GetProperty("action").GetString())
            .Should().BeEquivalentTo("Create", "Delete", "Create");
    }

    [Fact]
    public async Task Removing_values_that_do_not_exist_is_404()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var food = Food(book);

        (await client.DeleteAsync(Url(book, $"/{food}/default"), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.DeleteAsync(Url(book, $"/{food}/overrides/202601"), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await SetOverrideAsync(client, book, food, 202601, 1000m);
        (await client.DeleteAsync(Url(book, $"/{food}/default"), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.DeleteAsync(Url(book, $"/{food}/overrides/202602"), Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("固定支出", null)]
    [InlineData("主食", "午餐")]
    [InlineData("工作薪資", null)]
    public async Task Only_floating_expense_main_categories_can_have_budgets(string main, string? sub)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var category = book.FindCategory(main, sub)!.Id.Value;

        var response = await client.PutAsJsonAsync(Url(book, $"/{category}/default"), new { amount = 100m }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SettingsMaintenanceEndpointsTests.ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("rule");
    }

    [Fact]
    public async Task Negative_amount_and_invalid_month_are_400_and_zero_is_allowed()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var food = Food(book);

        var negative = await client.PutAsJsonAsync(Url(book, $"/{food}/default"), new { amount = -1m }, ApiJson.Options, Ct);
        var badMonth = await client.PutAsJsonAsync(Url(book, $"/{food}/overrides/202613"), new { amount = 1m }, ApiJson.Options, Ct);
        var badDelete = await client.DeleteAsync(Url(book, $"/{food}/overrides/202600"), Ct);

        negative.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SettingsMaintenanceEndpointsTests.ReadProblemAsync(negative)).GetProperty("errors").TryGetProperty("amount", out _).Should().BeTrue();
        badMonth.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SettingsMaintenanceEndpointsTests.ReadProblemAsync(badMonth)).GetProperty("errors").TryGetProperty("budgetMonth", out _).Should().BeTrue();
        badDelete.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SetOverrideAsync(client, book, food, 202601, 0m)).Overrides.Should().Equal(new BudgetOverrideDto(202601, 0m));
    }

    [Fact]
    public async Task Locked_months_can_still_be_budgeted()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var food = Food(book);
        (await client.PutAsJsonAsync($"/api/books/{book.Id.Value}/lock-date", new { lockDate = "2026-03-31" }, ApiJson.Options, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 預算不產生分錄，不檢查鎖帳日（Q2）
        await SetOverrideAsync(client, book, food, 202601, 1000m);
        (await client.DeleteAsync(Url(book, $"/{food}/overrides/202601"), Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    internal static string Url(Book book, string suffix = "") => $"/api/books/{book.Id.Value}/budgets{suffix}";

    internal static Task<CategoryBudgetDto> SetDefaultAsync(HttpClient client, Book book, Guid categoryId, decimal amount) =>
        PutAsync(client, Url(book, $"/{categoryId}/default"), amount);

    internal static Task<CategoryBudgetDto> SetOverrideAsync(HttpClient client, Book book, Guid categoryId, int budgetMonth, decimal amount) =>
        PutAsync(client, Url(book, $"/{categoryId}/overrides/{budgetMonth}"), amount);

    /// <summary>新增一個沒有子分類的浮動主分類；回應形狀同 <see cref="SettingsMaintenanceEndpointsTests.AddAccountAsync"/>。</summary>
    internal static async Task<Guid> AddFloatingAsync(HttpClient client, Book book, string name)
    {
        var response = await client.PostAsJsonAsync($"/api/books/{book.Id.Value}/categories",
            new { name, kind = "Expense", nature = "Floating" }, ApiJson.Options, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private static Guid Food(Book book) => book.FindCategory("主食")!.Id.Value;

    private static async Task<CategoryBudgetDto> PutAsync(HttpClient client, string url, decimal amount)
    {
        var response = await client.PutAsJsonAsync(url, new { amount }, ApiJson.Options, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<CategoryBudgetDto>(ApiJson.Options, Ct))!;
    }

    private static async Task<List<JsonElement>> AuditAsync(HttpClient client, Book book, Guid entityId) =>
        [.. (await client.GetFromJsonAsync<JsonElement>($"/api/books/{book.Id.Value}/audit?entityId={entityId}", ApiJson.Options, Ct)).EnumerateArray()];

    private static bool IsNull(JsonElement entry, string property) =>
        !entry.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null;
}
