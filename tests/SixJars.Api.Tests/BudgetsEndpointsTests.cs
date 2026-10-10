using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Application.Planning;
using SixJars.Domain.Books;
using SixJars.Domain.Planning;
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

    [Fact]
    public async Task Budgets_list_floating_main_categories_with_budget_actual_and_remaining()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var food = Food(book);
        var lunch = book.FindCategory("主食", "午餐")!.Id.Value;
        var transport = await AddFloatingAsync(client, book, "交通");
        await SetDefaultAsync(client, book, food, 5000m);
        await SetOverrideAsync(client, book, food, 202602, 3000m);
        await SetOverrideAsync(client, book, transport, 202601, 1000m);
        await ExpenseAsync(client, book, "國泰Combo卡", lunch, -800m, "2026-02-10");
        await ExpenseAsync(client, book, "悠遊卡", lunch, -60m, "2026-02-11");          // 電子錢包的消費要算（ADR 0008）
        await ExpenseAsync(client, book, "現金", food, -100m, "2026-02-12");
        await ExpenseAsync(client, book, "現金", transport, -200m, "2026-02-13");
        await ExpenseAsync(client, book, "現金", food, -40m, "2026-01-31", budgetMonth: 202602);
        await ExpenseAsync(client, book, "現金", food, -999m, "2026-02-01", budgetMonth: 202603);

        var february = await GetSheetAsync(client, book, 202602);

        february.Rows.Should().Equal(
            new BudgetRowDto(food, 5000m, 3000m, BudgetSource.Override, 1000m, 2000m),
            new BudgetRowDto(transport, null, null, null, 200m, null));
        // 交通沒有 2 月的預算，它的 200 不算進已用（D6）
        february.Totals.Should().Be(new BudgetTotalsDto(3000m, 1000m, 2000m));
    }

    [Fact]
    public async Task Month_without_override_uses_default_and_unset_categories_are_listed()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var food = Food(book);
        var transport = await AddFloatingAsync(client, book, "交通");
        await SetDefaultAsync(client, book, food, 5000m);
        await SetOverrideAsync(client, book, transport, 202601, 1000m);

        var march = await GetSheetAsync(client, book, 202603);
        var january = await GetSheetAsync(client, book, 202601);

        march.Rows.Should().Equal(
            new BudgetRowDto(food, 5000m, 5000m, BudgetSource.Default, 0m, 5000m),
            new BudgetRowDto(transport, null, null, null, 0m, null));
        january.Rows.Should().Contain(new BudgetRowDto(transport, null, 1000m, BudgetSource.Override, 0m, 1000m));
    }

    [Fact]
    public async Task Archived_category_is_listed_only_with_actual_or_override()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var transport = await AddFloatingAsync(client, book, "交通");
        await SetDefaultAsync(client, book, transport, 800m);
        await SetOverrideAsync(client, book, transport, 202602, 500m);
        await ExpenseAsync(client, book, "現金", transport, -50m, "2026-03-05");
        (await client.PostAsync($"/api/books/{book.Id.Value}/categories/{transport}/archive", null, Ct)).EnsureSuccessStatusCode();

        (await GetSheetAsync(client, book, 202601)).Rows.Select(r => r.CategoryId).Should().Equal(Food(book));
        (await GetSheetAsync(client, book, 202602)).Rows.Select(r => r.CategoryId).Should().Equal(Food(book), transport);
        (await GetSheetAsync(client, book, 202603)).Rows.Select(r => r.CategoryId).Should().Equal(Food(book), transport);
    }

    [Fact]
    public async Task Budgets_use_at_most_four_database_round_trips()
    {
        var counter = new CommandCounter();
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct, services => services.AddSingleton<IInterceptor>(counter));
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        await SetDefaultAsync(client, book, Food(book), 5000m);
        await SetOverrideAsync(client, book, Food(book), 202602, 3000m);
        await SetOverrideAsync(client, book, Food(book), 202603, 3000m);
        await ExpenseAsync(client, book, "現金", Food(book), -100m, "2026-02-12");

        counter.Reset();
        await GetSheetAsync(client, book, 202602);

        // 成員授權 1 次（BookAccessBehavior）+ 帳本 1 次 + 預算（含覆寫值）1 次 + 支出彙總 1 次（spec §5.2）。
        counter.Count.Should().BeLessThanOrEqualTo(4);
    }

    [Theory]
    [InlineData("?budgetMonth=202600")]
    [InlineData("?budgetMonth=202613")]
    public async Task Invalid_budget_month_is_400(string query)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await (await factory.CreateMemberClientAsync()).GetAsync(Url(book, query), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SettingsMaintenanceEndpointsTests.ReadProblemAsync(response)).GetProperty("errors").TryGetProperty("budgetMonth", out _).Should().BeTrue();
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

    private static async Task<BudgetSheetDto> GetSheetAsync(HttpClient client, Book book, int budgetMonth)
    {
        var response = await client.GetAsync(Url(book, $"?budgetMonth={budgetMonth}"), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<BudgetSheetDto>(ApiJson.Options, Ct))!;
    }

    private static async Task ExpenseAsync(HttpClient client, Book book, string account, Guid categoryId, decimal amount, string date, int? budgetMonth = null)
    {
        var response = await client.PostAsJsonAsync($"/api/books/{book.Id.Value}/transactions",
            new { kind = "Expense", date, amount, accountId = book.FindAccount(account)!.Id.Value, categoryId, budgetMonth }, ApiJson.Options, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
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
