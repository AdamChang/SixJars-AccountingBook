using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Application.Auditing;
using SixJars.Application.Books;
using SixJars.Domain.Books;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

/// <summary>刪除帳本（issue #6、ADR 0010）：硬刪除整本帳本，只留一筆刪除摘要的稽核記錄。</summary>
public class DeleteBookEndpointsTests(PostgresFixture postgres)
{
    private const string SecondOwner = "second-owner-sub";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Preview_counts_active_rows_members_and_audit_entries()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        await SeedEverythingAsync(factory, client, book);

        var preview = await client.GetFromJsonAsync<BookDeletionPreview>(PreviewUrl(book), ApiJson.Options, Ct);

        // D3：已軟刪除的交易與預定支出不列入；預定支出是手動一筆＋週期產生一筆。
        preview.Should().Be(new BookDeletionPreview(
            "測試帳本", new DateOnly(2025, 12, 31), new DateOnly(2026, 1, 31),
            Transactions: 1, PlannedExpenses: 2, RecurringPlannedExpenses: 1, CategoryBudgets: 1, Members: 2,
            AuditEntries: (await AuditTrailTests.EntriesAsync(factory)).Count(e => e.BookId == book.Id.Value)));
    }

    /// <summary>D6：有 BookId 欄位、不是 owned 的資料表（依 ordinal 排序）。新增這類資料表時，convention test 會失敗：要同時更新 DeleteBookHandler 與 SeedEverythingAsync。</summary>
    internal static readonly string[] BookScopedTables =
        ["AuditEntries", "BookMembers", "CategoryBudgets", "PlannedExpenses", "RecurringPlannedExpenses", "Transactions"];

    /// <summary>Book 的 owned 集合，由 FK cascade 刪除。Postings、CategoryBudgetOverrides 沒有 BookId，由 FK 保證不會成為孤兒。</summary>
    private static readonly string[] OwnedTables = ["Accounts", "Categories", "PlanningFunds"];

    [Fact]
    public async Task Book_scoped_tables_match_the_model()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();

        var tables = db.Model.GetEntityTypes()
            .Where(t => !t.IsOwned() && t.FindProperty("BookId") is not null)
            .Select(t => t.GetTableName()!)
            .Order(StringComparer.Ordinal);

        tables.Should().Equal(BookScopedTables, "新增帶 BookId 的資料表時，DeleteBookHandler 與 SeedEverythingAsync 都要一起更新");
    }

    [Fact]
    public async Task Delete_removes_every_book_scoped_row_and_keeps_other_books()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        // 另一本同名、同擁有者的帳本：刪除拿掉 BookId 條件、或改用名稱比對，都會連帶刪到它。
        var other = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        await SeedEverythingAsync(factory, client, book);
        await SeedEverythingAsync(factory, client, other);
        var otherBefore = await RowCountsAsync(factory, other);
        otherBefore.Values.Should().OnlyContain(count => count > 0, "seed 必須涵蓋每張表，否則「刪光」可能是空轉通過");

        var response = await DeleteAsync(client, book, "測試帳本");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await RowCountsAsync(factory, book)).Should().OnlyContain(
            pair => pair.Value == (pair.Key == "AuditEntries" ? 1 : 0), "只留下一筆刪除摘要；已軟刪除的資料也要刪掉");
        (await RowCountsAsync(factory, other)).Should().Equal(otherBefore);
    }

    [Fact]
    public async Task Delete_leaves_a_single_summary_entry_by_the_signed_in_owner()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        await SeedEverythingAsync(factory, client, book);
        var preview = await client.GetFromJsonAsync<BookDeletionPreview>(PreviewUrl(book), ApiJson.Options, Ct);

        (await DeleteAsync(client, book, "測試帳本")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var entry = (await AuditTrailTests.EntriesAsync(factory)).Should().ContainSingle(e => e.BookId == book.Id.Value).Subject;
        (entry.Action, entry.EntityType, entry.EntityId, entry.ActorSubject)
            .Should().Be((AuditAction.Delete, AuditEntityTypes.Book, book.Id.Value, ApiFactory.DefaultSubject));
        entry.After.Should().BeNull();
        // Before 只放摘要，不放交易內容（Q8、Q15）；就是刪除前 preview 的那組數字（D4）。
        JsonSerializer.Deserialize<BookDeletionPreview>(entry.Before!, ApiJson.Options).Should().Be(preview);
    }

    [Fact]
    public async Task Any_owner_can_delete()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        await factory.AddOwnerAsync(book.Id, SecondOwner, Ct);

        var response = await DeleteAsync(await factory.CreateMemberClientAsync(SecondOwner), book, "測試帳本");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("測試帳本 ")]
    [InlineData("別的帳本")]
    public async Task Delete_with_a_different_name_is_422_and_deletes_nothing(string confirmName)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();

        var response = await DeleteAsync(client, book, confirmName);

        // D2：ordinal 完全比對、不 trim。
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SettingsMaintenanceEndpointsTests.ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("name-mismatch");
        (await RowCountsAsync(factory, book))["Books"].Should().Be(1);
    }

    [Fact]
    public async Task Delete_without_confirm_name_is_400()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        using var request = new HttpRequestMessage(HttpMethod.Delete, BookUrl(book)) { Content = JsonContent.Create(new { }) };

        var response = await client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").TryGetProperty("confirmName", out _).Should().BeTrue();
    }

    /// <summary>
    /// 每張帶 BookId 的資料表都放資料（D6）：已軟刪除的交易與預定支出、由週期項目產生的預定支出（Restrict FK）、
    /// 預算的預設值與覆寫值（owned 表）、第二位擁有者，以及鎖帳日（Q10：鎖帳日不擋刪除）。
    /// </summary>
    internal static async Task SeedEverythingAsync(ApiFactory factory, HttpClient client, Book book)
    {
        await AuditTrailTests.CreateTransactionAsync(client, book, AuditTrailTests.LunchInput(book, -100m, "留著"));
        var deleted = await AuditTrailTests.CreateTransactionAsync(client, book, AuditTrailTests.LunchInput(book, -50m, "刪掉"));
        await ExpectNoContentAsync(client.DeleteAsync(
            $"/api/books/{book.Id.Value}/transactions/{deleted.Id}?version={deleted.Version}", Ct));

        await PlannedExpensesEndpointsTests.CreateAsync(
            client, book, PlannedExpensesEndpointsTests.InsuranceInput(book, 202602, -3000m, "留著"));
        var plannedDeleted = await PlannedExpensesEndpointsTests.CreateAsync(
            client, book, PlannedExpensesEndpointsTests.InsuranceInput(book, 202603, -1000m, "刪掉"));
        await ExpectNoContentAsync(client.DeleteAsync(
            $"{PlannedExpensesEndpointsTests.Url(book)}/{plannedDeleted.Id}?version={plannedDeleted.Version}", Ct));

        await RecurringPlannedExpensesEndpointsTests.CreateAsync(client, book, RecurringPlannedExpensesEndpointsTests.Insurance(book));
        await PlannedExpenseGenerationEndpointsTests.GenerateAsync(client, book, 202601);

        var food = book.FindCategory("主食")!.Id.Value;
        await BudgetsEndpointsTests.SetDefaultAsync(client, book, food, 5000m);
        await BudgetsEndpointsTests.SetOverrideAsync(client, book, food, 202602, 3000m);

        await factory.AddOwnerAsync(book.Id, SecondOwner, Ct);
        await ExpectNoContentAsync(client.PutAsJsonAsync($"/api/books/{book.Id.Value}/lock-date", new { lockDate = "2026-01-31" }, Ct));
    }

    private static async Task<HttpResponseMessage> DeleteAsync(HttpClient client, Book book, string confirmName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, BookUrl(book)) { Content = JsonContent.Create(new { confirmName }) };
        return await client.SendAsync(request, Ct);
    }

    /// <summary>以 raw SQL 計數：不套 query filter，已軟刪除的資料也算在內。表名來自上面的常數清單，不是外部輸入。</summary>
    private static async Task<Dictionary<string, int>> RowCountsAsync(ApiFactory factory, Book book)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
        var counts = new Dictionary<string, int>();
        foreach (var table in BookScopedTables.Concat(OwnedTables))
        {
            counts[table] = await db.Database
                .SqlQueryRaw<int>($"SELECT count(*)::int AS \"Value\" FROM \"{table}\" WHERE \"BookId\" = {{0}}", book.Id.Value)
                .SingleAsync(Ct);
        }

        counts["Books"] = await db.Database
            .SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM \"Books\" WHERE \"Id\" = {0}", book.Id.Value)
            .SingleAsync(Ct);
        return counts;
    }

    private static string BookUrl(Book book) => $"/api/books/{book.Id.Value}";

    private static string PreviewUrl(Book book) => $"/api/books/{book.Id.Value}/deletion-preview";

    private static async Task ExpectNoContentAsync(Task<HttpResponseMessage> request) =>
        (await request).StatusCode.Should().Be(HttpStatusCode.NoContent);
}
