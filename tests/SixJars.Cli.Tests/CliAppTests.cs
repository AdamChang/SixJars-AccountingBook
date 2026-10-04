using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Api.Tests;
using SixJars.Application.Auditing;
using SixJars.Application.Backup;
using SixJars.Application.Books;
using SixJars.Application.Transactions;
using SixJars.Domain.Books;
using SixJars.Domain.Members;
using SixJars.Domain.Transactions;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Cli.Tests;

/// <summary>CLI 在 process 內執行（<see cref="CliApp.RunAsync"/>），以 exit code、輸出與資料庫內容判斷結果。</summary>
public class CliAppTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 1, 2, 3, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Missing_connection_string_exits_with_2()
    {
        var output = new StringWriter();

        var exitCode = await CliApp.RunAsync(
            ["add-member", "--book", Guid.NewGuid().ToString(), "--email", "owner@example.com"],
            new Dictionary<string, string?>(), output, Ct);

        exitCode.Should().Be(2);
        output.ToString().Should().Contain("ConnectionStrings__SixJars");
    }

    [Fact]
    public async Task Add_member_creates_owner_and_audit_entry()
    {
        var connectionString = await postgres.CreateConnectionStringAsync(Ct);
        var bookId = await SeedBookAsync(connectionString);
        var output = new StringWriter();

        var exitCode = await RunAsync(connectionString, output, "add-member", "--book", bookId.ToString(), "--email", " Owner@Example.com ");

        exitCode.Should().Be(0, output.ToString());
        await using var db = Open(connectionString);
        var member = await db.BookMembers.SingleAsync(Ct);
        member.BookId.Value.Should().Be(bookId);
        member.Email.Should().Be("owner@example.com");
        member.Role.Should().Be(BookRole.Owner);
        // 尚未綁定：第一次以這個 email 登入 Google 時才綁定 sub（T36）。
        member.GoogleSubject.Should().BeNull();
        member.AddedAt.Should().Be(Now);

        var entry = await db.AuditEntries.SingleAsync(Ct);
        entry.Action.Should().Be(AuditAction.Create);
        entry.EntityType.Should().Be(AuditEntityTypes.BookMember);
        entry.EntityId.Should().Be(member.Id);
        entry.BookId.Should().Be(bookId);
        entry.ActorSubject.Should().Be(CliCurrentUser.CliSubject);
        entry.ActorSubject.Should().Be("cli");
        entry.At.Should().Be(Now);
        entry.Before.Should().BeNull();
        entry.After.Should().Contain("owner@example.com");
    }

    [Fact]
    public async Task Add_member_to_missing_book_fails_with_clear_message()
    {
        var connectionString = await postgres.CreateConnectionStringAsync(Ct);
        var missing = Guid.NewGuid();
        var output = new StringWriter();

        var exitCode = await RunAsync(connectionString, output, "add-member", "--book", missing.ToString(), "--email", "owner@example.com");

        exitCode.Should().Be(1);
        output.ToString().Should().Contain(missing.ToString()).And.Contain("找不到帳本").And.NotContain("   at ");
        await using var db = Open(connectionString);
        (await db.BookMembers.CountAsync(Ct)).Should().Be(0);
        (await db.AuditEntries.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task Add_member_twice_fails_with_clear_message()
    {
        var connectionString = await postgres.CreateConnectionStringAsync(Ct);
        var bookId = await SeedBookAsync(connectionString);
        (await RunAsync(connectionString, new StringWriter(), "add-member", "--book", bookId.ToString(), "--email", "owner@example.com"))
            .Should().Be(0);
        var output = new StringWriter();

        // 大小寫不同仍是同一個 email（Domain 正規化），不能靠資料庫的唯一索引擲出原始的 DbUpdateException。
        var exitCode = await RunAsync(connectionString, output, "add-member", "--book", bookId.ToString(), "--email", "OWNER@example.com");

        exitCode.Should().Be(1);
        output.ToString().Should().Contain("owner@example.com").And.Contain("已是").And.NotContain("   at ");
        await using var db = Open(connectionString);
        (await db.BookMembers.CountAsync(Ct)).Should().Be(1);
        (await db.AuditEntries.CountAsync(Ct)).Should().Be(1);
    }

    [Fact]
    public async Task Invalid_email_is_rejected_without_writing()
    {
        var connectionString = await postgres.CreateConnectionStringAsync(Ct);
        var bookId = await SeedBookAsync(connectionString);
        var output = new StringWriter();

        var exitCode = await RunAsync(connectionString, output, "add-member", "--book", bookId.ToString(), "--email", "not-an-email");

        exitCode.Should().Be(1);
        output.ToString().Should().Contain("Email").And.NotContain("   at ");
        await using var db = Open(connectionString);
        (await db.BookMembers.CountAsync(Ct)).Should().Be(0);
    }

    /// <summary>連線失敗時的錯誤訊息不可帶出連線字串（含密碼），也不印 stack trace。</summary>
    [Fact]
    public async Task Database_errors_never_print_the_connection_string()
    {
        const string password = "pw-must-not-leak-7f3a";
        var connectionString = $"Host=127.0.0.1;Port=1;Database=nope;Username=nobody;Password={password};Timeout=2";
        var output = new StringWriter();

        var exitCode = await RunAsync(connectionString, output, "add-member", "--book", Guid.NewGuid().ToString(), "--email", "owner@example.com");

        exitCode.Should().NotBe(0);
        output.ToString().Should().NotContain(password).And.NotContain(connectionString).And.NotContain("   at ")
            .And.NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Import_legacy_with_missing_file_fails_with_clear_message()
    {
        var connectionString = await postgres.CreateConnectionStringAsync(Ct);
        var missing = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsm");
        var output = new StringWriter();

        var exitCode = await RunAsync(connectionString, output,
            "import-legacy", "--file", missing, "--book-name", "我的帳本", "--owner-email", "owner@example.com");

        exitCode.Should().Be(1);
        output.ToString().Should().Contain("找不到檔案").And.NotContain("   at ");
        await using var db = Open(connectionString);
        (await db.Books.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task Restore_backup_writes_book_and_restore_audit_entry()
    {
        var connectionString = await postgres.CreateConnectionStringAsync(Ct);
        var backup = MinimalBackup();
        var file = await WriteBackupFileAsync(JsonSerializer.Serialize(backup, BackupJson.Options));
        var output = new StringWriter();

        var exitCode = await RunAsync(connectionString, output, "restore-backup", "--file", file);

        exitCode.Should().Be(0, output.ToString());
        output.ToString().Should().Contain(backup.Book.Id.ToString()).And.Contain(Path.GetFileName(file))
            .And.NotContain(Path.GetDirectoryName(file)!);
        await using var db = Open(connectionString);
        var book = await db.Books.SingleAsync(Ct);
        book.Id.Value.Should().Be(backup.Book.Id);
        book.LockDate.Should().Be(backup.Book.LockDate);
        var transaction = await db.Transactions.SingleAsync(Ct);
        transaction.Id.Value.Should().Be(backup.Transactions[0].Transaction.Id);
        transaction.Amount.Should().Be(-80m);
        var member = await db.BookMembers.SingleAsync(Ct);
        member.Id.Should().Be(backup.Members[0].Id);
        member.GoogleSubject.Should().Be("owner-sub");

        var entries = await db.AuditEntries.OrderBy(e => e.At).ToListAsync(Ct);
        entries.Should().HaveCount(2);
        entries[0].Id.Should().Be(backup.AuditEntries[0].Id);
        entries[0].ActorSubject.Should().Be("owner-sub");
        var restore = entries[1];
        restore.Action.Should().Be(AuditAction.Restore);
        restore.ActorSubject.Should().Be("cli");
        restore.At.Should().Be(Now);
        // 只記檔名，不記本機路徑。
        restore.After.Should().Contain(Path.GetFileName(file)).And.NotContain(Path.GetDirectoryName(file)!);
    }

    /// <summary>格式錯誤、或帳本已存在：exit code 1，訊息看得懂；不印 stack trace，也不印完整路徑。</summary>
    [Fact]
    public async Task Restore_backup_failures_are_reported_without_writing()
    {
        var connectionString = await postgres.CreateConnectionStringAsync(Ct);
        var broken = await WriteBackupFileAsync("{ \"formatVersion\": 1, \"book\": ");
        var valid = await WriteBackupFileAsync(JsonSerializer.Serialize(MinimalBackup(), BackupJson.Options));
        var missing = Path.Combine(Path.GetDirectoryName(valid)!, "missing.json");

        foreach (var (file, expected) in new[] { (broken, "不是有效的備份檔"), (missing, "找不到檔案") })
        {
            var output = new StringWriter();

            var exitCode = await RunAsync(connectionString, output, "restore-backup", "--file", file);

            exitCode.Should().Be(1);
            output.ToString().Should().Contain(expected).And.Contain(Path.GetFileName(file))
                .And.NotContain(Path.GetDirectoryName(file)!).And.NotContain("   at ");
        }

        await using (var db = Open(connectionString))
        {
            (await db.Books.CountAsync(Ct)).Should().Be(0);
        }

        (await RunAsync(connectionString, new StringWriter(), "restore-backup", "--file", valid)).Should().Be(0);
        var duplicate = new StringWriter();
        (await RunAsync(connectionString, duplicate, "restore-backup", "--file", valid)).Should().Be(1);
        duplicate.ToString().Should().Contain("已有").And.NotContain("   at ");
        await using var check = Open(connectionString);
        (await check.AuditEntries.CountAsync(e => e.Action == AuditAction.Restore, Ct)).Should().Be(1);
    }

    /// <summary>一本只有一個帳戶、一個分類、一筆交易、一位已綁定成員與一筆稽核記錄的備份。</summary>
    private static BackupDocument MinimalBackup()
    {
        var bookId = Guid.CreateVersion7();
        var cash = new AccountDto(Guid.CreateVersion7(), "現金", AccountType.Cash, 1000m, true);
        var food = new CategoryDto(Guid.CreateVersion7(), "主食", CategoryKind.Expense, ExpenseNature.Floating, null);
        var transactionId = Guid.CreateVersion7();
        var transaction = new TransactionDto(transactionId, TransactionKind.Expense, new DateOnly(2026, 1, 5), 202601, -80m, cash.Id,
            null, food.Id, null, null, null, "午餐", [new PostingDto(cash.Id, -80m)], 0);
        using var after = JsonDocument.Parse(JsonSerializer.Serialize(transaction, AuditSnapshots.Options));
        return new BackupDocument(
            BackupDocument.CurrentFormatVersion,
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            new BookDto(bookId, "還原的帳本", new DateOnly(2025, 12, 31), new DateOnly(2026, 1, 31), [cash], [], [food]),
            [new BackupTransaction(transaction, null)],
            [],
            [new BackupMember(Guid.CreateVersion7(), "owner-sub@example.com", "owner-sub", BookRole.Owner, new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero))],
            [new AuditEntryDto(Guid.CreateVersion7(), new DateTimeOffset(2026, 1, 5, 1, 0, 0, TimeSpan.Zero), "owner-sub", AuditAction.Create,
                AuditEntityTypes.Transaction, transactionId, null, after.RootElement.Clone())]);
    }

    private static async Task<string> WriteBackupFileAsync(string content)
    {
        var directory = Directory.CreateTempSubdirectory("sixjars-restore-");
        var file = Path.Combine(directory.FullName, "sixjars-backup-20260201.json");
        await File.WriteAllTextAsync(file, content, Ct);
        return file;
    }

    private static Task<int> RunAsync(string connectionString, TextWriter output, params string[] args) =>
        CliApp.RunAsync(
            args,
            new Dictionary<string, string?> { [CliApp.ConnectionStringVariable] = connectionString },
            output,
            Ct,
            services => services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now)));

    private static async Task<Guid> SeedBookAsync(string connectionString)
    {
        var book = new Book("測試帳本", new DateOnly(2025, 12, 31));
        await using var db = Open(connectionString);
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return book.Id.Value;
    }

    private static SixJarsDbContext Open(string connectionString) =>
        new(new DbContextOptionsBuilder<SixJarsDbContext>().UseNpgsql(connectionString).Options);
}
