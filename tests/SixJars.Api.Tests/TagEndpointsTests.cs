using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Application.Books;
using SixJars.Domain.Books;
using SixJars.Domain.Transactions;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;
using static SixJars.Api.Tests.SettingsMaintenanceEndpointsTests;

namespace SixJars.Api.Tests;

/// <summary>標籤設定：新增、修改、封存、排序、刪除（tags backend spec）。</summary>
public class TagEndpointsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Added_tags_are_listed_in_sort_order_with_color_name()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();

        var response = await client.PostAsJsonAsync(Url(book, "/tags"), new { name = " 日本旅遊 ", color = "Teal" }, ApiJson.Options, Ct);
        await AddTagAsync(client, book, "可報帳", "Amber");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var raw = await client.GetFromJsonAsync<JsonElement>(Url(book, ""), ApiJson.Options, Ct);
        var tags = raw.GetProperty("tags").EnumerateArray().ToList();
        tags.Select(t => t.GetProperty("name").GetString()).Should().Equal("日本旅遊", "可報帳");
        tags.Select(t => t.GetProperty("color").GetString()).Should().Equal("Teal", "Amber");
        tags.Select(t => t.GetProperty("sortOrder").GetInt32()).Should().Equal(0, 1);
    }

    [Theory]
    [InlineData(" ", "Red")]
    [InlineData("一二三四五六七八九", "Red")]
    [InlineData("旅遊", "Magenta")]
    public async Task Invalid_name_or_color_is_400(string name, string color)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();

        var response = await client.PostAsJsonAsync(Url(book, "/tags"), new { name, color }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Eight_emoji_name_is_allowed()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();

        var response = await client.PostAsJsonAsync(Url(book, "/tags"), new { name = "👨‍👩‍👧‍👦✈️✈️✈️✈️✈️✈️✈️", color = "Red" }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Name_differing_only_by_case_is_422()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        await AddTagAsync(client, book, "Trip");

        var response = await client.PostAsJsonAsync(Url(book, "/tags"), new { name = "TRIP", color = "Red" }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("rule");
    }

    [Fact]
    public async Task Update_renames_and_recolors_with_audit()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var trip = await AddTagAsync(client, book, "旅遊", "Teal");

        var response = await client.PutAsJsonAsync(Url(book, $"/tags/{trip}"), new { name = "日本旅遊", color = "Pink" }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var dto = (await client.GetFromJsonAsync<BookDto>(Url(book, ""), ApiJson.Options, Ct))!;
        dto.Tags.Should().ContainSingle().Which.Should().Match<TagDto>(t => t.Name == "日本旅遊" && t.Color == TagColor.Pink);
        var history = await client.GetFromJsonAsync<JsonElement>(Url(book, $"/audit?entityId={trip}"), ApiJson.Options, Ct);
        var entry = history.EnumerateArray().Single(e => e.GetProperty("action").GetString() == "Update");
        entry.GetProperty("entityType").GetString().Should().Be("Tag");
        entry.GetProperty("before").GetProperty("name").GetString().Should().Be("旅遊");
        entry.GetProperty("after").GetProperty("color").GetString().Should().Be("Pink");
    }

    [Fact]
    public async Task Update_unknown_tag_is_422()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();

        var response = await client.PutAsJsonAsync(Url(book, $"/tags/{Guid.NewGuid()}"), new { name = "旅遊", color = "Red" }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Archive_and_unarchive_tag()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var trip = await AddTagAsync(client, book, "旅遊");

        (await client.PostAsync(Url(book, $"/tags/{trip}/archive"), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetTagsAsync(client, book)).Single().ArchivedAt.Should().NotBeNull();

        (await client.PostAsync(Url(book, $"/tags/{trip}/unarchive"), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetTagsAsync(client, book)).Single().ArchivedAt.Should().BeNull();
    }

    [Fact]
    public async Task Archiving_or_removing_a_tag_does_not_touch_categories()
    {
        // ArchiveSetting／RemoveSetting 的 default 分支是分類：標籤不能落進去。
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var lunch = book.FindCategory("主食", "午餐")!.Id.Value;

        (await client.PostAsync(Url(book, $"/tags/{lunch}/archive"), null, Ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await client.DeleteAsync(Url(book, $"/tags/{lunch}"), Ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var dto = (await client.GetFromJsonAsync<BookDto>(Url(book, ""), ApiJson.Options, Ct))!;
        dto.Categories.Should().Contain(c => c.Id == lunch && c.ArchivedAt == null);
    }

    [Fact]
    public async Task Reorder_tags_records_audit_only_for_moved_tags()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var a = await AddTagAsync(client, book, "A");
        var b = await AddTagAsync(client, book, "B");
        var c = await AddTagAsync(client, book, "C");

        var response = await client.PutAsJsonAsync(Url(book, "/tags/order"), new { ids = new[] { b, a, c } }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetTagsAsync(client, book)).Select(t => t.Id).Should().Equal(b, a, c);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
        (await db.AuditEntries.Where(e => e.EntityType == "Tag" && e.Action == Application.Auditing.AuditAction.Update)
            .Select(e => e.EntityId).ToListAsync(Ct)).Should().BeEquivalentTo([a, b]);
    }

    [Fact]
    public async Task Remove_unused_tag()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var a = await AddTagAsync(client, book, "A");
        var b = await AddTagAsync(client, book, "B");

        (await client.DeleteAsync(Url(book, $"/tags/{a}"), Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await GetTagsAsync(client, book)).Should().ContainSingle().Which.Should().Match<TagDto>(t => t.Id == b && t.SortOrder == 0);
    }

    [Fact]
    public async Task Tag_used_only_by_deleted_transaction_cannot_be_removed()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var trip = await AddTagAsync(client, book, "旅遊");
        await SeedDeletedTaggedExpenseAsync(factory, book.Id, trip);

        var response = await client.DeleteAsync(Url(book, $"/tags/{trip}"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("in-use");
    }

    internal static async Task<Guid> AddTagAsync(HttpClient client, Book book, string name, string color = "Red")
    {
        var response = await client.PostAsJsonAsync(Url(book, "/tags"), new { name, color }, ApiJson.Options, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ApiJson.Options, Ct)).GetProperty("id").GetGuid();
    }

    private static async Task<IReadOnlyList<TagDto>> GetTagsAsync(HttpClient client, Book book) =>
        (await client.GetFromJsonAsync<BookDto>(Url(book, ""), ApiJson.Options, Ct))!.Tags!;

    // 直接寫資料庫：交易掛標籤的 API 在另一組測試（TransactionTagEndpointsTests）。
    private static async Task SeedDeletedTaggedExpenseAsync(ApiFactory factory, Domain.Common.BookId bookId, Guid tagId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
        var book = await db.Books.SingleAsync(b => b.Id == bookId, Ct);
        var transactions = new TransactionFactory(book);
        var expense = transactions.WithTags(
            transactions.Expense(new DateOnly(2026, 1, 5), book.FindAccount("現金")!.Id, book.FindCategory("主食")!.Id, -100m),
            [new Domain.Common.TagId(tagId)]);
        expense.Delete(DateTimeOffset.UtcNow);
        db.Transactions.Add(expense);
        await db.SaveChangesAsync(Ct);
    }
}
