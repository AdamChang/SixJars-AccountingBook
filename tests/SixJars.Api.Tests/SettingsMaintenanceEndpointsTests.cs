using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Application.Books;
using SixJars.Application.Ledger;
using SixJars.Domain.Books;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

/// <summary>帳本設定的維護：排序、改名、封存、刪除（P4 段 J）。</summary>
public class SettingsMaintenanceEndpointsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Book_lists_settings_in_sort_order()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        await ReverseAccountsAndExpenseMainsAsync(factory, book);
        var client = await factory.CreateMemberClientAsync();

        var dto = (await client.GetFromJsonAsync<BookDto>(Url(book, ""), ApiJson.Options, Ct))!;

        dto.Accounts.Select(a => a.Name).Should().Equal("房屋貸款", "悠遊卡", "國泰Combo卡", "國泰世華銀行", "現金");
        dto.Accounts.Select(a => a.SortOrder).Should().Equal(0, 1, 2, 3, 4);
        // 前序：收入主分類 → 支出主分類（每個主分類後接它的子分類）
        dto.Categories.Select(c => c.Name).Should().Equal("工作薪資", "貸款支出", "房屋貸款", "固定支出", "保險費", "主食", "午餐");
        dto.Accounts.Should().OnlyContain(a => a.ArchivedAt == null);
    }

    [Fact]
    public async Task Summary_lists_accounts_in_sort_order()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        await ReverseAccountsAndExpenseMainsAsync(factory, book);
        var client = await factory.CreateMemberClientAsync();

        var summary = (await client.GetFromJsonAsync<LedgerSummaryDto>(
            Url(book, "/summary?budgetMonth=202602&asOf=2026-02-20"), ApiJson.Options, Ct))!;

        summary.Accounts.Select(a => a.Name).Should().Equal("房屋貸款", "悠遊卡", "國泰Combo卡", "國泰世華銀行", "現金");
    }

    [Fact]
    public async Task Rename_account_and_change_cash_flag()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var cash = book.FindAccount("現金")!;

        var response = await client.PutAsJsonAsync(Url(book, $"/accounts/{cash.Id.Value}"),
            new { name = "零用金", countsAsAvailableCash = false }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var dto = (await client.GetFromJsonAsync<BookDto>(Url(book, ""), ApiJson.Options, Ct))!;
        dto.Accounts.Should().ContainSingle(a => a.Id == cash.Id.Value && a.Name == "零用金" && !a.CountsAsAvailableCash);
    }

    [Fact]
    public async Task Rename_to_existing_name_is_422_and_blank_is_400()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var cash = book.FindAccount("現金")!.Id.Value;

        var duplicate = await client.PutAsJsonAsync(Url(book, $"/accounts/{cash}"),
            new { name = "國泰世華銀行", countsAsAvailableCash = true }, ApiJson.Options, Ct);
        var blank = await client.PutAsJsonAsync(Url(book, $"/planning-funds/{book.PlanningFunds[0].Id.Value}"),
            new { name = " " }, ApiJson.Options, Ct);

        duplicate.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemAsync(duplicate)).GetProperty("code").GetString().Should().Be("rule");
        blank.StatusCode.Should().Be(HttpStatusCode.BadRequest);   // 空白名稱由 validator 擋下
    }

    [Fact]
    public async Task Change_nature_of_expense_main_category()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var food = book.FindCategory("主食")!;

        var response = await client.PutAsJsonAsync(Url(book, $"/categories/{food.Id.Value}"),
            new { name = "飲食", nature = "Special" }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var dto = (await client.GetFromJsonAsync<BookDto>(Url(book, ""), ApiJson.Options, Ct))!;
        dto.Categories.Where(c => c.Id == food.Id.Value || c.ParentId == food.Id.Value)
            .Should().HaveCount(2).And.OnlyContain(c => c.Nature == ExpenseNature.Special).And.Contain(c => c.Name == "飲食");
    }

    [Fact]
    public async Task Category_with_deleted_planned_expense_cannot_become_floating()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        // 預定支出掛在子分類「保險費」；刪除後仍算參照（P4 J plan D3、D6）
        var planned = await PlannedExpensesEndpointsTests.CreateAsync(client, book,
            PlannedExpensesEndpointsTests.InsuranceInput(book, 202602, -1200m, "保險"));
        (await client.DeleteAsync(Url(book, $"/planned-expenses/{planned.Id}?version={planned.Version}"), Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var fixedMain = book.FindCategory("固定支出")!;

        var response = await client.PutAsJsonAsync(Url(book, $"/categories/{fixedMain.Id.Value}"),
            new { name = "固定支出", nature = "Floating" }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("in-use");
    }

    [Fact]
    public async Task Update_writes_audit_entry_with_before_and_after()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var fund = book.PlanningFunds[0];

        await client.PutAsJsonAsync(Url(book, $"/planning-funds/{fund.Id.Value}"), new { name = "自由基金" }, ApiJson.Options, Ct);

        var history = await client.GetFromJsonAsync<JsonElement>(Url(book, $"/audit?entityId={fund.Id.Value}"), ApiJson.Options, Ct);
        var entry = history.EnumerateArray().Should().ContainSingle().Subject;
        entry.GetProperty("action").GetString().Should().Be("Update");
        entry.GetProperty("entityType").GetString().Should().Be("PlanningFund");
        entry.GetProperty("before").GetProperty("name").GetString().Should().Be("財務自由帳戶");
        entry.GetProperty("after").GetProperty("name").GetString().Should().Be("自由基金");
    }

    [Fact]
    public async Task Archive_and_unarchive_zero_balance_account()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var postOffice = await AddAccountAsync(client, book, "郵局");

        (await client.PostAsync(Url(book, $"/accounts/{postOffice}/archive"), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var archived = (await client.GetFromJsonAsync<BookDto>(Url(book, ""), ApiJson.Options, Ct))!;
        archived.Accounts.Single(a => a.Id == postOffice).ArchivedAt.Should().NotBeNull();

        (await client.PostAsync(Url(book, $"/accounts/{postOffice}/unarchive"), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var restored = (await client.GetFromJsonAsync<BookDto>(Url(book, ""), ApiJson.Options, Ct))!;
        restored.Accounts.Single(a => a.Id == postOffice).ArchivedAt.Should().BeNull();
    }

    [Fact]
    public async Task Account_with_balance_cannot_be_archived()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await (await factory.CreateMemberClientAsync()).PostAsync(
            Url(book, $"/accounts/{book.FindAccount("現金")!.Id.Value}/archive"), null, Ct);   // 期初 1000

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("non-zero-balance");
    }

    [Fact]
    public async Task Archive_main_category_and_planning_fund()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var food = book.FindCategory("主食")!.Id.Value;

        (await client.PostAsync(Url(book, $"/categories/{food}/archive"), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var fund = await client.PostAsync(Url(book, $"/planning-funds/{book.PlanningFunds[0].Id.Value}/archive"), null, Ct);

        fund.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);   // 財務自由帳戶期初 10000
        (await ReadProblemAsync(fund)).GetProperty("code").GetString().Should().Be("non-zero-balance");
        var dto = (await client.GetFromJsonAsync<BookDto>(Url(book, ""), ApiJson.Options, Ct))!;
        dto.Categories.Single(c => c.Id == food).ArchivedAt.Should().NotBeNull();
        dto.Categories.Single(c => c.Name == "午餐").ArchivedAt.Should().BeNull();
    }

    [Fact]
    public async Task Archive_unknown_id_is_422()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await (await factory.CreateMemberClientAsync()).PostAsync(Url(book, $"/categories/{Guid.NewGuid()}/archive"), null, Ct);

        // 與既有的 GetCategory 一致：找不到設定項目是領域錯誤（422），不是 404
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Reorder_accounts()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var reversed = book.Accounts.OrderByDescending(a => a.SortOrder).Select(a => a.Id.Value).ToArray();

        var response = await client.PutAsJsonAsync(Url(book, "/accounts/order"), new { ids = reversed }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var dto = (await client.GetFromJsonAsync<BookDto>(Url(book, ""), ApiJson.Options, Ct))!;
        dto.Accounts.Select(a => a.Id).Should().Equal(reversed);
    }

    [Fact]
    public async Task Reorder_sub_categories_of_a_parent()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var food = book.FindCategory("主食")!.Id.Value;
        await client.PostAsJsonAsync(Url(book, "/categories"), new { name = "晚餐", kind = "Expense", parentId = food }, ApiJson.Options, Ct);
        var subs = (await client.GetFromJsonAsync<BookDto>(Url(book, ""), ApiJson.Options, Ct))!.Categories.Where(c => c.ParentId == food).ToList();

        var response = await client.PutAsJsonAsync(Url(book, "/categories/order"),
            new { parentId = food, ids = subs.Select(c => c.Id).Reverse() }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var dto = (await client.GetFromJsonAsync<BookDto>(Url(book, ""), ApiJson.Options, Ct))!;
        dto.Categories.Where(c => c.ParentId == food).Select(c => c.Name).Should().Equal("晚餐", "午餐");
    }

    [Fact]
    public async Task Reorder_main_categories_of_a_kind()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var mains = book.Categories.Where(c => c.IsMain && c.Kind == CategoryKind.Expense).OrderByDescending(c => c.SortOrder).ToList();

        var response = await client.PutAsJsonAsync(Url(book, "/categories/order"),
            new { kind = "Expense", ids = mains.Select(c => c.Id.Value) }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var dto = (await client.GetFromJsonAsync<BookDto>(Url(book, ""), ApiJson.Options, Ct))!;
        dto.Categories.Where(c => c.ParentId == null && c.Kind == CategoryKind.Expense).Select(c => c.Name)
            .Should().Equal("貸款支出", "固定支出", "主食");
    }

    [Fact]
    public async Task Reorder_records_audit_only_for_moved_items()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var ids = book.Accounts.OrderBy(a => a.SortOrder).Select(a => a.Id.Value).ToList();
        (ids[0], ids[1]) = (ids[1], ids[0]);   // 只交換前兩個

        await client.PutAsJsonAsync(Url(book, "/accounts/order"), new { ids }, ApiJson.Options, Ct);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
        (await db.AuditEntries.Where(e => e.EntityType == "Account").Select(e => e.EntityId).ToListAsync(Ct))
            .Should().BeEquivalentTo(ids.Take(2));
    }

    [Fact]
    public async Task Stale_order_is_422()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await (await factory.CreateMemberClientAsync()).PutAsJsonAsync(Url(book, "/accounts/order"),
            new { ids = book.Accounts.Skip(1).Select(a => a.Id.Value) }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Main_category_order_requires_kind()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await (await factory.CreateMemberClientAsync()).PutAsJsonAsync(Url(book, "/categories/order"),
            new { ids = Array.Empty<Guid>() }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblemAsync(response)).GetProperty("errors").TryGetProperty("kind", out _).Should().BeTrue();
    }

    internal static async Task<Guid> AddAccountAsync(HttpClient client, Book book, string name)
    {
        var response = await client.PostAsJsonAsync(Url(book, "/accounts"), new { name, type = "Bank", openingBalance = 0m }, ApiJson.Options, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    internal static string Url(Book book, string path) => $"/api/books/{book.Id.Value}{path}";

    /// <summary>直接在資料庫裡把順序倒過來，證明輸出是依 SortOrder，而不是碰巧依插入順序。</summary>
    private static async Task ReverseAccountsAndExpenseMainsAsync(ApiFactory factory, Book book)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
        var tracked = await db.Books.SingleAsync(b => b.Id == book.Id, Ct);
        tracked.ReorderAccounts([.. tracked.Accounts.OrderByDescending(a => a.SortOrder).Select(a => a.Id)]);
        tracked.ReorderCategories(CategoryKind.Expense, parentId: null,
            [.. tracked.Categories.Where(c => c.IsMain && c.Kind == CategoryKind.Expense).OrderByDescending(c => c.SortOrder).Select(c => c.Id)]);
        await db.SaveChangesAsync(Ct);
    }

    internal static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }
}
