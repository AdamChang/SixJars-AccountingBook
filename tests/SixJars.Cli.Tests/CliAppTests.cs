using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Api.Tests;
using SixJars.Application.Auditing;
using SixJars.Domain.Books;
using SixJars.Domain.Members;
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
