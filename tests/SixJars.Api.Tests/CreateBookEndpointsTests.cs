using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Application.Auditing;
using SixJars.Application.Members;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Members;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

/// <summary>建立帳本（issue #7、ADR 0011）：建立者建立空白帳本並成為擁有者；每人最多 5 本，同一位擁有者不能同名。</summary>
public class CreateBookEndpointsTests(PostgresFixture postgres)
{
    private const string Creator = "creator-sub";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Creator_creates_an_empty_book_and_becomes_its_bound_owner()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await factory.AddCreatorAsync(Creator, Ct);
        var client = await factory.CreateMemberClientAsync(Creator);

        var response = await CreateAsync(client, " 家庭帳本 ", "2026-10-19");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await ReadIdAsync(response);
        response.Headers.Location!.OriginalString.Should().Be($"/api/books/{id}");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SixJarsDbContext>();
        var book = await db.Books.AsNoTracking().SingleAsync(b => b.Id == new BookId(id), Ct);
        (book.Name, book.OpeningDate).Should().Be(("家庭帳本", new DateOnly(2026, 10, 19)));
        book.Accounts.Should().BeEmpty("Q13：空白帳本，帳戶由使用者在設定頁新增");
        var owner = await db.BookMembers.AsNoTracking().SingleAsync(m => m.BookId == new BookId(id), Ct);
        (owner.Email, owner.GoogleSubject, owner.Role).Should().Be((TestAuthHandler.EmailOf(Creator), Creator, BookRole.Owner));
    }

    [Fact]
    public async Task Create_is_audited_with_name_and_opening_date()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await factory.AddCreatorAsync(Creator, Ct);
        var client = await factory.CreateMemberClientAsync(Creator);

        var id = await ReadIdAsync(await CreateAsync(client, "家庭帳本", "2026-10-19"));

        var entry = (await AuditTrailTests.EntriesAsync(factory)).Should().ContainSingle().Subject;
        (entry.BookId, entry.Action, entry.EntityType, entry.EntityId, entry.ActorSubject)
            .Should().Be((id, AuditAction.Create, AuditEntityTypes.Book, id, Creator));
        entry.Before.Should().BeNull();
        entry.After.Should().Contain("家庭帳本").And.Contain("2026-10-19");
    }

    [Fact]
    public async Task Member_who_is_not_a_creator_gets_403()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();

        var response = await CreateAsync(client, "家庭帳本", "2026-10-19");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await BookCountAsync(factory)).Should().Be(1);
    }

    [Fact]
    public async Task Sixth_owned_book_is_rejected()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await factory.AddCreatorAsync(Creator, Ct);
        for (var i = 0; i < 5; i++)
        {
            await factory.SeedBookAsync(Ct, ownerSubject: Creator);
        }

        var response = await CreateAsync(await factory.CreateMemberClientAsync(Creator), "第六本", "2026-10-19");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SettingsMaintenanceEndpointsTests.ReadProblemAsync(response)).GetProperty("code").GetString()
            .Should().Be(DomainException.BookQuotaExceededCode);
        (await BookCountAsync(factory)).Should().Be(5);
    }

    [Fact]
    public async Task Same_name_for_the_same_owner_is_rejected()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await factory.AddCreatorAsync(Creator, Ct);
        await factory.SeedBookAsync(Ct, ownerSubject: Creator);

        var response = await CreateAsync(await factory.CreateMemberClientAsync(Creator), " 測試帳本 ", "2026-10-19");

        // Q9：比對 trim 後的名稱（Book 會 trim）。
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await SettingsMaintenanceEndpointsTests.ReadProblemAsync(response)).GetProperty("code").GetString()
            .Should().Be(DomainException.DuplicateNameCode);
    }

    [Fact]
    public async Task Same_name_owned_by_someone_else_is_allowed()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await factory.AddCreatorAsync(Creator, Ct);
        await factory.SeedBookAsync(Ct);

        var response = await CreateAsync(await factory.CreateMemberClientAsync(Creator), "測試帳本", "2026-10-19");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Blank_name_is_400(string? name)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await factory.AddCreatorAsync(Creator, Ct);
        var client = await factory.CreateMemberClientAsync(Creator);

        var response = await client.PostAsJsonAsync("/api/books", new { name, openingDate = "2026-10-19" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").TryGetProperty("name", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Name_length_is_checked_after_trimming()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await factory.AddCreatorAsync(Creator, Ct);
        var client = await factory.CreateMemberClientAsync(Creator);

        // Book 存的是 trim 後的名稱，長度上限也要以 trim 後為準：前後的空白不算字數。
        var fits = await CreateAsync(client, $"  {new string('帳', 100)}  ", "2026-10-19");
        var tooLong = await CreateAsync(client, $"  {new string('本', 101)}  ", "2026-10-19");

        fits.StatusCode.Should().Be(HttpStatusCode.Created);
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await tooLong.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").TryGetProperty("name", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Missing_opening_date_is_400()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await factory.AddCreatorAsync(Creator, Ct);
        var client = await factory.CreateMemberClientAsync(Creator);

        // 沒帶 openingDate 會綁成 default(DateOnly)＝0001-01-01，必須被擋下而不是建出開帳日 0001 的帳本。
        var response = await client.PostAsJsonAsync("/api/books", new { name = "家庭帳本" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").TryGetProperty("openingDate", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Concurrent_creates_cannot_exceed_the_quota()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await factory.AddCreatorAsync(Creator, Ct);
        for (var i = 0; i < 4; i++)
        {
            await factory.SeedBookAsync(Ct, ownerSubject: Creator);
        }

        var first = await factory.CreateMemberClientAsync(Creator);
        var second = await factory.CreateMemberClientAsync(Creator);

        var responses = await Task.WhenAll(CreateAsync(first, "甲", "2026-10-19"), CreateAsync(second, "乙", "2026-10-19"));

        // Q16：advisory lock 讓第二個請求等第一個 commit 之後才計數。沒有鎖時不一定每次重現，所以這個測試只能保證「有鎖時一定綠」。
        responses.Select(r => r.StatusCode).Order()
            .Should().Equal(HttpStatusCode.Created, HttpStatusCode.UnprocessableEntity);
        (await BookCountAsync(factory)).Should().Be(5);
    }

    [Fact]
    public async Task Me_reports_remaining_quota_for_a_creator()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await factory.AddCreatorAsync(Creator, Ct);
        await factory.SeedBookAsync(Ct, ownerSubject: Creator);
        await factory.SeedBookAsync(Ct, ownerSubject: Creator);

        var me = await (await factory.CreateMemberClientAsync(Creator)).GetFromJsonAsync<MeDto>("/api/me", ApiJson.Options, Ct);

        (me!.CanCreateBooks, me.RemainingBookQuota).Should().Be((true, 3));
    }

    [Fact]
    public async Task Me_reports_no_quota_for_a_member_who_is_not_a_creator()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await factory.SeedBookAsync(Ct);

        var me = await (await factory.CreateMemberClientAsync()).GetFromJsonAsync<MeDto>("/api/me", ApiJson.Options, Ct);

        (me!.CanCreateBooks, me.RemainingBookQuota).Should().Be((false, 0));
    }

    [Fact]
    public async Task Me_quota_is_never_negative()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        await factory.AddCreatorAsync(Creator, Ct);
        // Q8：被 add-member 加成擁有者而超過上限時不擋，只是不能再自己建立。
        for (var i = 0; i < 6; i++)
        {
            await factory.SeedBookAsync(Ct, ownerSubject: Creator);
        }

        var me = await (await factory.CreateMemberClientAsync(Creator)).GetFromJsonAsync<MeDto>("/api/me", ApiJson.Options, Ct);

        (me!.CanCreateBooks, me.RemainingBookQuota).Should().Be((true, 0));
    }

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, string name, string openingDate) =>
        client.PostAsJsonAsync("/api/books", new { name, openingDate }, Ct);

    private static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private static async Task<int> BookCountAsync(ApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SixJarsDbContext>().Books.CountAsync(Ct);
    }
}
