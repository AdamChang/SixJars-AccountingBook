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
