using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SixJars.Application.Transactions;
using SixJars.Domain.Books;
using SixJars.Tests.Shared;
using Xunit;
using static SixJars.Api.Tests.SettingsMaintenanceEndpointsTests;
using static SixJars.Api.Tests.TagEndpointsTests;

namespace SixJars.Api.Tests;

/// <summary>交易掛標籤（tags backend spec G2、G3、G7、G12、D2、D4、D5）。</summary>
public class TransactionTagEndpointsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Created_transaction_returns_tag_ids_sorted_by_guid()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var trip = await AddTagAsync(client, book, "旅遊");
        var work = await AddTagAsync(client, book, "工作");
        Guid[] sorted = [.. new[] { trip, work }.Order()];

        var created = await CreateAsync(client, book, Lunch(book, [sorted[1], sorted[0]]));

        created.TagIds.Should().Equal(sorted);
        (await GetAsync(client, book, created.Id)).TagIds.Should().Equal(sorted);
    }

    [Fact]
    public async Task Transaction_without_tag_ids_has_empty_list()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();

        var created = await CreateAsync(client, book, Lunch(book, null));

        var raw = await client.GetFromJsonAsync<JsonElement>(Url(book, $"/transactions/{created.Id}"), ApiJson.Options, Ct);
        raw.GetProperty("tagIds").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Non_income_expense_kinds_can_have_tags()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var trip = await AddTagAsync(client, book, "旅遊");
        var cash = book.FindAccount("現金")!.Id.Value;
        var bank = book.FindAccount("國泰世華銀行")!.Id.Value;

        var withdrawal = await CreateAsync(client, book, new
        {
            kind = "Withdrawal", date = "2026-01-05", amount = 20000m, accountId = bank, counterAccountId = cash, tagIds = new[] { trip },
        });
        var topUp = await CreateAsync(client, book, new
        {
            kind = "TopUp", date = "2026-01-05", amount = 500m, accountId = book.FindAccount("悠遊卡")!.Id.Value, counterAccountId = cash,
            tagIds = new[] { trip },
        });
        var fundWithdrawal = await CreateAsync(client, book, new
        {
            kind = "FundWithdrawal", date = "2026-01-05", amount = 3000m, accountId = bank,
            planningFundId = book.PlanningFunds[0].Id.Value, tagIds = new[] { trip },
        });

        new[] { withdrawal, topUp, fundWithdrawal }.Should().OnlyContain(t => t.TagIds!.SequenceEqual(new[] { trip }));
    }

    [Fact]
    public async Task More_than_four_or_duplicate_tags_is_400()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var ids = new List<Guid>();
        foreach (var name in new[] { "A", "B", "C", "D", "E" })
        {
            ids.Add(await AddTagAsync(client, book, name));
        }

        var five = await PostAsync(client, book, Lunch(book, [.. ids]));
        var duplicate = await PostAsync(client, book, Lunch(book, [ids[0], ids[0]]));

        five.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        duplicate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Tag_of_another_book_is_422()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var other = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var theirs = await AddTagAsync(client, other, "旅遊");

        var response = await PostAsync(client, book, Lunch(book, [theirs]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Archived_tag_can_be_kept_but_not_newly_added()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var trip = await AddTagAsync(client, book, "旅遊");
        var work = await AddTagAsync(client, book, "工作");
        var created = await CreateAsync(client, book, Lunch(book, [trip]));
        await ArchiveAsync(client, book, trip);
        await ArchiveAsync(client, book, work);

        var createWithArchived = await PostAsync(client, book, Lunch(book, [trip]));
        var keep = await PutAsync(client, book, created, Lunch(book, [trip], "改備註"));
        var kept = (await keep.Content.ReadFromJsonAsync<TransactionDto>(ApiJson.Options, Ct))!;
        var addArchived = await PutAsync(client, book, kept, Lunch(book, [trip, work]));

        createWithArchived.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        keep.StatusCode.Should().Be(HttpStatusCode.OK);
        kept.TagIds.Should().Equal(trip);
        addArchived.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Update_without_tag_ids_clears_tags()
    {
        // spec D4：TransactionInput 是「送什麼存什麼」，沒帶 tagIds 就是清空。
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var trip = await AddTagAsync(client, book, "旅遊");
        var created = await CreateAsync(client, book, Lunch(book, [trip]));

        var response = await PutAsync(client, book, created, Lunch(book, null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetAsync(client, book, created.Id)).TagIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Changing_tags_of_locked_transaction_is_422_locked()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var trip = await AddTagAsync(client, book, "旅遊");
        var created = await CreateAsync(client, book, Lunch(book, null));
        (await client.PutAsJsonAsync(Url(book, "/lock-date"), new { lockDate = "2026-01-31" }, ApiJson.Options, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await PutAsync(client, book, created, Lunch(book, [trip]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().Should().Be("locked");
    }

    [Fact]
    public async Task Changing_only_tags_with_stale_version_is_409()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var trip = await AddTagAsync(client, book, "旅遊");
        var work = await AddTagAsync(client, book, "工作");
        var created = await CreateAsync(client, book, Lunch(book, null));

        var first = await PutAsync(client, book, created, Lunch(book, [trip]));
        var second = await PutAsync(client, book, created, Lunch(book, [work]));

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await GetAsync(client, book, created.Id)).TagIds.Should().Equal(trip);
    }

    [Fact]
    public async Task Audit_snapshots_include_tag_ids()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var trip = await AddTagAsync(client, book, "旅遊");
        var work = await AddTagAsync(client, book, "工作");
        var created = await CreateAsync(client, book, Lunch(book, [trip]));

        await PutAsync(client, book, created, Lunch(book, [work]));

        var history = await client.GetFromJsonAsync<JsonElement>(Url(book, $"/audit?entityId={created.Id}"), ApiJson.Options, Ct);
        var update = history.EnumerateArray().Single(e => e.GetProperty("action").GetString() == "Update");
        TagIdsOf(update.GetProperty("before")).Should().Equal(trip);
        TagIdsOf(update.GetProperty("after")).Should().Equal(work);
    }

    [Fact]
    public async Task List_filters_by_tag()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var other = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var a = await AddTagAsync(client, book, "A");
        var b = await AddTagAsync(client, book, "B");
        var theirs = await AddTagAsync(client, other, "A");
        var onlyA = await CreateAsync(client, book, Lunch(book, [a]));
        var both = await CreateAsync(client, book, Lunch(book, [a, b]));
        await CreateAsync(client, book, Lunch(book, null));

        (await ListAsync(client, book, $"?budgetMonth=202601&tagId={a}")).Select(t => t.Id).Should().Equal(onlyA.Id, both.Id);
        (await ListAsync(client, book, $"?budgetMonth=202601&tagId={b}")).Select(t => t.Id).Should().Equal(both.Id);
        // 與 accountId 篩選相同：別本帳的 Id 只是查不到，不是 404。
        (await ListAsync(client, book, $"?tagId={theirs}")).Should().BeEmpty();
        (await ListAsync(client, book, "?budgetMonth=202601")).Should().HaveCount(3);
    }

    private static async Task<IReadOnlyList<TransactionDto>> ListAsync(HttpClient client, Book book, string query)
    {
        var response = await client.GetAsync(Url(book, $"/transactions{query}"), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<TransactionDto>>(ApiJson.Options, Ct))!;
    }

    internal static object Lunch(Book book, Guid[]? tagIds, string note = "午餐") => new
    {
        kind = "Expense", date = "2026-01-05", amount = -120m, note, tagIds,
        accountId = book.FindAccount("現金")!.Id.Value, categoryId = book.FindCategory("主食", "午餐")!.Id.Value,
    };

    internal static async Task<TransactionDto> CreateAsync(HttpClient client, Book book, object input)
    {
        var response = await PostAsync(client, book, input);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TransactionDto>(ApiJson.Options, Ct))!;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, Book book, object input) =>
        client.PostAsJsonAsync(Url(book, "/transactions"), input, ApiJson.Options, Ct);

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, Book book, TransactionDto current, object input) =>
        client.PutAsJsonAsync(Url(book, $"/transactions/{current.Id}"), new { version = current.Version, input }, ApiJson.Options, Ct);

    private static async Task<TransactionDto> GetAsync(HttpClient client, Book book, Guid id) =>
        (await client.GetFromJsonAsync<TransactionDto>(Url(book, $"/transactions/{id}"), ApiJson.Options, Ct))!;

    private static async Task ArchiveAsync(HttpClient client, Book book, Guid tagId) =>
        (await client.PostAsync(Url(book, $"/tags/{tagId}/archive"), null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

    private static IEnumerable<Guid> TagIdsOf(JsonElement snapshot) =>
        snapshot.GetProperty("tagIds").EnumerateArray().Select(e => e.GetGuid());
}
