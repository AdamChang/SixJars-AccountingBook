using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SixJars.Application.Ledger;
using SixJars.Application.Transactions;
using SixJars.Domain.Books;
using SixJars.Domain.Transactions;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

public class TransactionsEndpointsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_expense_returns_201_and_get_returns_postings()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var cash = book.FindAccount("現金")!.Id.Value;
        var lunch = book.FindCategory("主食", "午餐")!.Id.Value;
        var client = await factory.CreateMemberClientAsync();

        var response = await client.PostAsJsonAsync($"/api/books/{book.Id.Value}/transactions",
            new { kind = "Expense", date = "2026-01-05", amount = -120m, accountId = cash, categoryId = lunch }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        created.TryGetProperty("version", out _).Should().BeTrue();
        var id = created.GetProperty("id").GetGuid();
        response.Headers.Location!.ToString().Should().Be($"/api/books/{book.Id.Value}/transactions/{id}");

        var dto = await client.GetFromJsonAsync<TransactionDto>($"/api/books/{book.Id.Value}/transactions/{id}", ApiJson.Options, Ct);
        dto!.Id.Should().Be(id);
        dto.Postings.Should().Equal(new PostingDto(cash, -120m));
        dto.BudgetMonth.Should().Be(202601);
    }

    [Fact]
    public async Task Transaction_of_another_book_is_404()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var mine = await factory.SeedBookAsync(Ct);
        var other = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var created = await client.PostAsJsonAsync($"/api/books/{other.Id.Value}/transactions",
            new
            {
                kind = "Expense", date = "2026-01-05", amount = -120m,
                accountId = other.FindAccount("現金")!.Id.Value, categoryId = other.FindCategory("主食", "午餐")!.Id.Value,
            },
            ApiJson.Options, Ct);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        // 用自己帳本的路徑讀別本帳的交易：只用交易 Id 查詢就會讀到（T21 審查的變異測試）
        var response = await client.GetAsync($"/api/books/{mine.Id.Value}/transactions/{id}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Unknown_transaction_is_404()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await (await factory.CreateMemberClientAsync()).GetAsync($"/api/books/{book.Id.Value}/transactions/{Guid.NewGuid()}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Transfer_to_credit_card_is_422()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await (await factory.CreateMemberClientAsync()).PostAsJsonAsync($"/api/books/{book.Id.Value}/transactions",
            new
            {
                kind = "Transfer",
                date = "2026-01-05",
                amount = 500m,
                accountId = book.FindAccount("國泰世華銀行")!.Id.Value,
                counterAccountId = book.FindAccount("國泰Combo卡")!.Id.Value,
            }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("code").GetString().Should().Be("rule");
    }

    [Fact]
    public async Task Transfer_without_counter_account_is_400()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await (await factory.CreateMemberClientAsync()).PostAsJsonAsync($"/api/books/{book.Id.Value}/transactions",
            new { kind = "Transfer", date = "2026-01-05", amount = 500m, accountId = book.FindAccount("國泰世華銀行")!.Id.Value },
            ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ReadProblemAsync(response);
        // key 與 body 欄位同名：camelCase、沒有 command 的 Input. 前綴，前端可直接對應表單欄位。
        problem.GetProperty("errors").EnumerateObject().Select(e => e.Name).Should().Equal("counterAccountId");
    }

    [Fact]
    public async Task List_is_ordered_by_date_then_creation()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var tenth = await CreateLunchAsync(client, book, "2026-01-10", "1/10");
        var fifth = await CreateLunchAsync(client, book, "2026-01-05", "先建立");
        var fifthLater = await CreateLunchAsync(client, book, "2026-01-05", "後建立");

        var list = await ListAsync(client, book, "");

        list.Select(t => t.Id).Should().Equal(fifth, fifthLater, tenth);
    }

    [Fact]
    public async Task List_filters_by_budget_month()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        await CreateLunchAsync(client, book, "2026-01-15", "1 月");
        var salary = await CreateAsync(client, book, new
        {
            kind = "Income", date = "2026-01-30", budgetMonth = 202602, amount = 50000m,
            accountId = book.FindAccount("國泰世華銀行")!.Id.Value, categoryId = book.FindCategory("工作薪資")!.Id.Value,
        });
        var february = await CreateLunchAsync(client, book, "2026-02-03", "2 月");

        var list = await ListAsync(client, book, "?budgetMonth=202602");

        list.Select(t => t.Id).Should().Equal(salary, february);
    }

    [Fact]
    public async Task List_filters_by_account_including_counter_account()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var cash = book.FindAccount("現金")!.Id.Value;
        var bank = book.FindAccount("國泰世華銀行")!.Id.Value;
        var expense = await CreateLunchAsync(client, book, "2026-01-05", "現金午餐");
        var transfer = await CreateAsync(client, book,
            new { kind = "Transfer", date = "2026-01-06", amount = 500m, accountId = bank, counterAccountId = cash });
        await CreateAsync(client, book, new
        {
            kind = "Expense", date = "2026-01-07", amount = -80m,
            accountId = bank, categoryId = book.FindCategory("主食", "午餐")!.Id.Value,
        });

        var list = await ListAsync(client, book, $"?accountId={cash}");

        list.Select(t => t.Id).Should().Equal(expense, transfer);
    }

    [Fact]
    public async Task List_by_account_of_another_book_returns_nothing()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var mine = await factory.SeedBookAsync(Ct);
        var other = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        await CreateLunchAsync(client, mine, "2026-01-05", "我的");
        await CreateLunchAsync(client, other, "2026-01-05", "別人的");

        // 用自己帳本的路徑、帶別本帳的帳戶 Id 篩選：只用帳戶條件查詢就會讀到別本帳的交易。
        var list = await ListAsync(client, mine, $"?accountId={other.FindAccount("現金")!.Id.Value}");

        list.Should().BeEmpty();
    }

    [Fact]
    public async Task List_filters_by_date_range_inclusive()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        await CreateLunchAsync(client, book, "2026-01-09", "區間前");
        var first = await CreateLunchAsync(client, book, "2026-01-10", "下限當天");
        var last = await CreateLunchAsync(client, book, "2026-01-20", "上限當天");
        await CreateLunchAsync(client, book, "2026-01-21", "區間後");

        var list = await ListAsync(client, book, "?from=2026-01-10&to=2026-01-20");

        list.Select(t => t.Id).Should().Equal(first, last);
    }

    [Fact]
    public async Task List_by_account_includes_same_account_fund_allocation_without_postings()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var bank = book.FindAccount("國泰世華銀行")!.Id.Value;
        var earmark = await CreateDtoAsync(client, book, new
        {
            kind = "FundAllocation", date = "2026-01-05", amount = 1000m,
            accountId = bank, planningFundId = book.FindPlanningFund("財務自由帳戶")!.Id.Value,
        });
        earmark.Postings.Should().BeEmpty("同帳戶圈存不產生分錄，只能靠 AccountId 條件篩到");

        var list = await ListAsync(client, book, $"?accountId={bank}");

        list.Select(t => t.Id).Should().Equal(earmark.Id);
    }

    [Fact]
    public async Task Same_day_order_stays_by_creation_after_update()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var earlier = await CreateDtoAsync(client, book, LunchInput(book, "2026-01-05", "先建立"));
        var later = await CreateLunchAsync(client, book, "2026-01-05", "後建立");
        // UPDATE 會把列搬到資料表尾端；沒有 ThenBy(Id) 時，同一天的順序就會跟著實體儲存順序變動
        (await client.PutAsJsonAsync($"/api/books/{book.Id.Value}/transactions/{earlier.Id}",
            new { version = earlier.Version, input = LunchInput(book, "2026-01-05", "先建立（改過）") }, ApiJson.Options, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await ListAsync(client, book, "");

        list.Select(t => t.Id).Should().Equal(earlier.Id, later);
    }

    [Fact]
    public async Task Invalid_update_reports_input_fields_without_prefix()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var created = await CreateDtoAsync(client, book, LunchInput(book, "2026-01-05", "午餐"));

        var response = await client.PutAsJsonAsync($"/api/books/{book.Id.Value}/transactions/{created.Id}",
            new
            {
                version = created.Version,
                input = new { kind = "Transfer", date = "2026-01-06", amount = 500m, accountId = book.FindAccount("國泰世華銀行")!.Id.Value },
            },
            ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ReadProblemAsync(response);
        // 新增與修改用同一套 key，前端的表單只需一種對應方式。
        problem.GetProperty("errors").EnumerateObject().Select(e => e.Name).Should().Equal("counterAccountId");
    }

    [Fact]
    public async Task Update_changes_kind_and_postings_and_bumps_version()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var cash = book.FindAccount("現金")!.Id.Value;
        var bank = book.FindAccount("國泰世華銀行")!.Id.Value;
        var created = await CreateDtoAsync(client, book, LunchInput(book, "2026-01-05", "午餐"));

        var response = await client.PutAsJsonAsync($"/api/books/{book.Id.Value}/transactions/{created.Id}",
            new
            {
                version = created.Version,
                input = new { kind = "Transfer", date = "2026-01-06", amount = 500m, accountId = bank, counterAccountId = cash },
            },
            ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<TransactionDto>(ApiJson.Options, Ct))!;
        updated.Id.Should().Be(created.Id);
        updated.Kind.Should().Be(TransactionKind.Transfer);
        updated.Postings.Should().BeEquivalentTo([new PostingDto(bank, -500m), new PostingDto(cash, 500m)]);
        updated.Version.Should().NotBe(created.Version);

        // 讀取單筆與清單回傳的版本，必須是修改後的版本，前端才能接著再改。
        var reloaded = await client.GetFromJsonAsync<TransactionDto>($"/api/books/{book.Id.Value}/transactions/{created.Id}", ApiJson.Options, Ct);
        reloaded!.Version.Should().Be(updated.Version);
        reloaded.Postings.Should().BeEquivalentTo(updated.Postings);
        (await ListAsync(client, book, "")).Single().Version.Should().Be(updated.Version);
    }

    [Fact]
    public async Task Update_with_stale_version_is_409()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var created = await CreateDtoAsync(client, book, LunchInput(book, "2026-01-05", "午餐"));
        var url = $"/api/books/{book.Id.Value}/transactions/{created.Id}";

        var first = await client.PutAsJsonAsync(url,
            new { version = created.Version, input = LunchInput(book, "2026-01-05", "第一次修改") }, ApiJson.Options, Ct);
        var second = await client.PutAsJsonAsync(url,
            new { version = created.Version, input = LunchInput(book, "2026-01-05", "第二次修改") }, ApiJson.Options, Ct);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemAsync(second)).GetProperty("status").GetInt32().Should().Be(409);
        var current = await client.GetFromJsonAsync<TransactionDto>(url, ApiJson.Options, Ct);
        current!.Note.Should().Be("第一次修改");
    }

    [Fact]
    public async Task Update_unknown_transaction_is_404()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);

        var response = await (await factory.CreateMemberClientAsync()).PutAsJsonAsync($"/api/books/{book.Id.Value}/transactions/{Guid.NewGuid()}",
            new { version = 1u, input = LunchInput(book, "2026-01-05", "午餐") }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_transaction_of_another_book_is_404_and_leaves_it_unchanged()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var mine = await factory.SeedBookAsync(Ct);
        var other = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var theirs = await CreateDtoAsync(client, other, LunchInput(other, "2026-01-05", "別人的午餐"));

        // 用自己帳本的路徑、自己帳本的帳戶與分類，去改別本帳的交易：只用交易 Id 查詢就會改到。
        var response = await client.PutAsJsonAsync($"/api/books/{mine.Id.Value}/transactions/{theirs.Id}",
            new { version = theirs.Version, input = LunchInput(mine, "2026-01-09", "被改掉了") }, ApiJson.Options, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var after = await client.GetFromJsonAsync<TransactionDto>($"/api/books/{other.Id.Value}/transactions/{theirs.Id}", ApiJson.Options, Ct);
        after.Should().BeEquivalentTo(theirs);
    }

    [Fact]
    public async Task Delete_transaction_is_204_and_disappears_from_list_and_summary()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var kept = await CreateDtoAsync(client, book, LunchInput(book, "2026-01-05", "留著"));
        var deleted = await CreateDtoAsync(client, book, LunchInput(book, "2026-01-06", "要刪除"));
        var url = $"/api/books/{book.Id.Value}/transactions/{deleted.Id}";
        var summaryUrl = $"/api/books/{book.Id.Value}/summary?budgetMonth=202601&asOf=2026-01-31";
        var before = await client.GetFromJsonAsync<LedgerSummaryDto>(summaryUrl, ApiJson.Options, Ct);
        before!.MonthlyDisposable.Should().Be(-240m);

        var response = await client.DeleteAsync($"{url}?version={deleted.Version}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ListAsync(client, book, "")).Select(t => t.Id).Should().Equal(kept.Id);
        (await client.GetAsync(url, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var after = await client.GetFromJsonAsync<LedgerSummaryDto>(summaryUrl, ApiJson.Options, Ct);
        after!.MonthlyDisposable.Should().Be(-120m);
        after.Accounts.Single(a => a.Name == "現金").Balance.Should().Be(1000m - 120m);
        // 已刪除的交易不能再刪一次，也不能修改：query filter 查不到，一律 404。
        (await client.DeleteAsync($"{url}?version={deleted.Version}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PutAsJsonAsync(url, new { version = deleted.Version, input = LunchInput(book, "2026-01-06", "復活") }, ApiJson.Options, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_transaction_with_stale_version_is_409()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var created = await CreateDtoAsync(client, book, LunchInput(book, "2026-01-05", "午餐"));
        var url = $"/api/books/{book.Id.Value}/transactions/{created.Id}";
        // 另一台裝置先改過，版本因此前進。
        (await client.PutAsJsonAsync(url, new { version = created.Version, input = LunchInput(book, "2026-01-05", "改過") }, ApiJson.Options, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await client.DeleteAsync($"{url}?version={created.Version}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.GetFromJsonAsync<TransactionDto>(url, ApiJson.Options, Ct))!.Note.Should().Be("改過");
    }

    [Fact]
    public async Task Delete_transaction_of_another_book_is_404_and_leaves_it()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, Ct);
        var mine = await factory.SeedBookAsync(Ct);
        var other = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        var theirs = await CreateDtoAsync(client, other, LunchInput(other, "2026-01-05", "別人的午餐"));

        // 用自己帳本的路徑去刪別本帳的交易：只用交易 Id 查詢就會刪到。
        var response = await client.DeleteAsync($"/api/books/{mine.Id.Value}/transactions/{theirs.Id}?version={theirs.Version}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ListAsync(client, other, "")).Should().ContainSingle().Which.Should().BeEquivalentTo(theirs);
    }

    private static object LunchInput(Book book, string date, string note) => new
    {
        kind = "Expense", date, amount = -120m, note,
        accountId = book.FindAccount("現金")!.Id.Value, categoryId = book.FindCategory("主食", "午餐")!.Id.Value,
    };

    private static async Task<TransactionDto> CreateDtoAsync(HttpClient client, Book book, object input)
    {
        var response = await client.PostAsJsonAsync($"/api/books/{book.Id.Value}/transactions", input, ApiJson.Options, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TransactionDto>(ApiJson.Options, Ct))!;
    }

    private static Task<Guid> CreateLunchAsync(HttpClient client, Book book, string date, string note) =>
        CreateAsync(client, book, LunchInput(book, date, note));

    private static async Task<Guid> CreateAsync(HttpClient client, Book book, object input)
    {
        var response = await client.PostAsJsonAsync($"/api/books/{book.Id.Value}/transactions", input, ApiJson.Options, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private static async Task<IReadOnlyList<TransactionDto>> ListAsync(HttpClient client, Book book, string query)
    {
        var response = await client.GetAsync($"/api/books/{book.Id.Value}/transactions{query}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<TransactionDto>>(ApiJson.Options, Ct))!;
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }
}
