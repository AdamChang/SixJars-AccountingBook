using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Application.Auditing;
using SixJars.Application.Backup;
using SixJars.Application.Planning;
using SixJars.Application.Transactions;
using SixJars.Domain.Books;
using SixJars.Domain.Members;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Api.Tests;

/// <summary>JSON 完整備份（CONTEXT.md「備份」）：包含已軟刪除的資料，只含這一本帳，同一份資料每次匯出的結果相同。</summary>
public class BackupExportTests(PostgresFixture postgres)
{
    /// <summary>UTC 10/4 17:30 是台北 10/5 01:30：檔名的日期必須是台北的日期。</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 17, 30, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Backup_contains_deleted_transactions_members_and_audit()
    {
        await using var factory = await CreateFactoryAsync();
        var book = await factory.SeedBookAsync(Ct);
        // 另一本帳（同一個擁有者）也有交易、已刪除的交易與稽核記錄：匯出時拿掉 BookId 條件就會混進來。
        var other = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        // 日期較晚的先建立：匯出依日期排序，而不是依建立順序。
        var later = await AuditTrailTests.CreateTransactionAsync(client, book, Lunch(book, "2026-01-20", -120m));
        var earlier = await AuditTrailTests.CreateTransactionAsync(client, book, Lunch(book, "2026-01-05", -80m));
        await DeleteAsync(client, $"/api/books/{book.Id.Value}/transactions/{later.Id}?version={later.Version}");
        var planned = await PlannedExpensesEndpointsTests.CreateAsync(
            client, book, PlannedExpensesEndpointsTests.InsuranceInput(book, 202602, -3000m, "保險費"));
        await DeleteAsync(client, $"{PlannedExpensesEndpointsTests.Url(book)}/{planned.Id}?version={planned.Version}");
        var otherTransaction = await AuditTrailTests.CreateTransactionAsync(client, other, Lunch(other, "2026-01-10", -50m));
        await DeleteAsync(client, $"/api/books/{other.Id.Value}/transactions/{otherTransaction.Id}?version={otherTransaction.Version}");
        await AuditTrailTests.CreateTransactionAsync(client, other, Lunch(other, "2026-01-11", -60m));
        var otherPlanned = await PlannedExpensesEndpointsTests.CreateAsync(
            client, other, PlannedExpensesEndpointsTests.InsuranceInput(other, 202602, -1000m, "保險費"));
        await DeleteAsync(client, $"{PlannedExpensesEndpointsTests.Url(other)}/{otherPlanned.Id}?version={otherPlanned.Version}");

        var response = await client.GetAsync($"/api/books/{book.Id.Value}/export/backup.json", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileName.Should().Be("sixjars-backup-20261005.json");
        var backup = (await response.Content.ReadFromJsonAsync<BackupDocument>(BackupJson.Options, Ct))!;

        backup.ExportedAt.Should().Be(Now);
        backup.Book.Id.Should().Be(book.Id.Value);
        backup.Book.Accounts.Should().HaveCount(book.Accounts.Count);
        backup.Book.Categories.Should().HaveCount(book.Categories.Count);

        // 已刪除的交易也在；依日期排序。
        backup.Transactions.Select(t => t.Transaction.Id).Should().Equal(earlier.Id, later.Id);
        backup.Transactions[0].DeletedAt.Should().BeNull();
        backup.Transactions[1].DeletedAt.Should().Be(Now);
        backup.Transactions[1].Transaction.Amount.Should().Be(-120m);
        backup.Transactions[1].Transaction.Postings.Should().ContainSingle().Which.Amount.Should().Be(-120m);
        backup.PlannedExpenses.Should().ContainSingle().Which.Should().Match<BackupPlannedExpense>(
            p => p.PlannedExpense.Id == planned.Id && p.DeletedAt == Now);

        var member = backup.Members.Should().ContainSingle().Subject;
        member.Email.Should().Be(TestAuthHandler.EmailOf(ApiFactory.DefaultSubject));
        member.GoogleSubject.Should().Be(ApiFactory.DefaultSubject);
        member.Role.Should().Be(BookRole.Owner);

        // 這本帳的 5 筆：交易新增 2、刪除 1，預定支出新增、刪除各 1；另一本帳的 5 筆不可混進來。
        backup.AuditEntries.Should().HaveCount(5);
        backup.AuditEntries.Select(e => e.EntityId).Should().OnlyContain(id => id == earlier.Id || id == later.Id || id == planned.Id);
        backup.AuditEntries.Select(e => e.Action).Should().Equal(
            AuditAction.Create, AuditAction.Create, AuditAction.Delete, AuditAction.Create, AuditAction.Delete);
        backup.AuditEntries[0].After!.Value.GetProperty("amount").GetDecimal().Should().Be(-120m);
        backup.AuditEntries[2].Before!.Value.GetProperty("id").GetGuid().Should().Be(later.Id);
        backup.AuditEntries[2].After.Should().BeNull();
    }

    /// <summary>格式版本固定為 1；檔案縮排、中文不跳脫，方便人工檢視。同一份資料匯出兩次，內容完全相同。</summary>
    [Fact]
    public async Task Backup_format_version_is_1()
    {
        await using var factory = await CreateFactoryAsync();
        var book = await factory.SeedBookAsync(Ct);
        var client = await factory.CreateMemberClientAsync();
        await AuditTrailTests.CreateTransactionAsync(client, book, Lunch(book, "2026-01-05", -80m));
        var url = $"/api/books/{book.Id.Value}/export/backup.json";

        var json = await client.GetStringAsync(url, Ct);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("formatVersion").GetInt32().Should().Be(1);
        BackupDocument.CurrentFormatVersion.Should().Be(1);
        json.Should().Contain("\n  \"formatVersion\": 1").And.Contain("測試帳本").And.Contain("\"kind\": \"Expense\"");
        (await client.GetStringAsync(url, Ct)).Should().Be(json);
    }

    [Fact]
    public async Task Backup_of_missing_book_is_404()
    {
        await using var factory = await CreateFactoryAsync();
        var client = await factory.CreateMemberClientAsync();

        var response = await client.GetAsync($"/api/books/{Guid.NewGuid()}/export/backup.json", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private Task<ApiFactory> CreateFactoryAsync() =>
        ApiFactory.CreateAsync(postgres, Ct, services => services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now)));

    private static object Lunch(Book book, string date, decimal amount) => new
    {
        kind = "Expense", date, amount, note = "午餐",
        accountId = book.FindAccount("現金")!.Id.Value, categoryId = book.FindCategory("主食", "午餐")!.Id.Value,
    };

    private static async Task DeleteAsync(HttpClient client, string url) =>
        (await client.DeleteAsync(url, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
}
