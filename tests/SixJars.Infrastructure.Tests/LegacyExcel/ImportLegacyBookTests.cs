using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Api.Tests;
using SixJars.Application;
using SixJars.Application.Auditing;
using SixJars.Application.Common;
using SixJars.Application.LegacyImport;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Members;
using SixJars.Infrastructure;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;
using static SixJars.Application.Tests.LegacyImport.LegacyWorkbookFactory;

namespace SixJars.Infrastructure.Tests.LegacyExcel;

/// <summary>正式匯入（spec §8.1）：以合成的舊記帳本經 MediatR 寫入真實的 PostgreSQL。</summary>
public class ImportLegacyBookTests(PostgresFixture postgres)
{
    private const string Actor = "importer-sub";
    private const string BookName = "我們家的帳本";
    private static readonly DateOnly Jan6 = new(2026, 1, 6);
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 1, 2, 3, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>3 筆交易（收入、支出、已付的固定支出）與 1 筆預定支出。</summary>
    private static LegacyWorkbook ValidWorkbook(params LegacyJournalRow[] extraRows) => Workbook(Month(1,
        journal: [Row(7, Jan6, "國泰世華銀行", "工作薪資1", null, 84223m), Row(8, Jan6, "現金", "主食", "中餐", -80m), .. extraRows],
        templates: [new(52, LegacyTemplateSection.Fixed, "行動電話費", "國泰世華銀行", -599m, new DateOnly(2026, 1, 16), null)]));

    [Fact]
    public async Task Report_errors_write_nothing()
    {
        await using var host = await Host.CreateAsync(postgres);

        var result = await host.SendAsync(Import(ValidWorkbook(Row(14, Jan6, "不存在的銀行", "主食", "中餐", -80m))));

        result.BookId.Should().BeNull();
        result.Report.Errors.Should().ContainSingle(i => i.Row == 14);
        await host.ShouldBeEmptyAsync();
    }

    [Fact]
    public async Task Duplicate_book_name_is_rejected()
    {
        await using var host = await Host.CreateAsync(postgres);
        await using (var db = host.Open())
        {
            db.Books.Add(new Book(BookName, new DateOnly(2025, 12, 31)));
            await db.SaveChangesAsync(Ct);
        }

        var act = () => host.SendAsync(Import(ValidWorkbook()));

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().Contain(BookName);
        await using var check = host.Open();
        (await check.Books.CountAsync(Ct)).Should().Be(1);
        (await check.Transactions.IgnoreQueryFilters().CountAsync(Ct)).Should().Be(0);
        (await check.PlannedExpenses.IgnoreQueryFilters().CountAsync(Ct)).Should().Be(0);
        (await check.BookMembers.CountAsync(Ct)).Should().Be(0);
        (await check.AuditEntries.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task Dry_run_reports_counts_and_writes_nothing()
    {
        await using var host = await Host.CreateAsync(postgres);

        var result = await host.SendAsync(Import(ValidWorkbook()) with { DryRun = true });

        result.BookId.Should().BeNull();
        result.Report.Errors.Should().BeEmpty();
        result.Transactions.Should().Be(3);
        result.PlannedExpenses.Should().Be(1);
        await host.ShouldBeEmptyAsync();
    }

    [Fact]
    public async Task Successful_import_writes_book_ledger_owner_and_one_audit_entry()
    {
        await using var host = await Host.CreateAsync(postgres);

        var result = await host.SendAsync(Import(ValidWorkbook()));

        result.Report.Errors.Should().BeEmpty();
        result.Transactions.Should().Be(3);
        result.PlannedExpenses.Should().Be(1);
        result.BookId.Should().NotBeNull();
        var bookId = new BookId(result.BookId!.Value);

        await using var db = host.Open();
        var book = await db.Books.SingleAsync(Ct);
        book.Id.Should().Be(bookId);
        book.Name.Should().Be(BookName);
        book.FindAccount("國泰世華銀行").Should().NotBeNull();
        (await db.Transactions.CountAsync(t => t.BookId == bookId, Ct)).Should().Be(3);
        (await db.PlannedExpenses.CountAsync(p => p.BookId == bookId, Ct)).Should().Be(1);

        var owner = await db.BookMembers.SingleAsync(Ct);
        owner.BookId.Should().Be(bookId);
        owner.Email.Should().Be("owner@example.com");
        owner.Role.Should().Be(BookRole.Owner);
        // 尚未綁定：擁有者第一次以 Google 登入時才綁定 sub（T36）。
        owner.GoogleSubject.Should().BeNull();
        owner.AddedAt.Should().Be(Now);

        var entry = await db.AuditEntries.SingleAsync(Ct);
        entry.Action.Should().Be(AuditAction.Import);
        entry.EntityType.Should().Be(AuditEntityTypes.Book);
        entry.EntityId.Should().Be(bookId.Value);
        entry.BookId.Should().Be(bookId.Value);
        entry.ActorSubject.Should().Be(Actor);
        entry.At.Should().Be(Now);
        entry.Before.Should().BeNull();
        // 只記檔名：本機路徑可能含使用者名稱等個資。
        entry.After.Should().NotContain("私人資料夾").And.NotContain("someone");
        using var after = JsonDocument.Parse(entry.After!);
        after.RootElement.GetProperty("fileName").GetString().Should().Be("2026帳本v1.xlsm");
        after.RootElement.GetProperty("transactions").GetInt32().Should().Be(3);
        after.RootElement.GetProperty("plannedExpenses").GetInt32().Should().Be(1);
        after.RootElement.GetProperty("warnings").GetInt32().Should().Be(result.Report.Warnings.Count);
    }

    /// <summary>寫入分成多次 SaveChanges；中途失敗時，整個 DB transaction 回滾，任何一張表都不留下資料。</summary>
    [Fact]
    public async Task Failure_midway_leaves_nothing_behind()
    {
        await using var host = await Host.CreateAsync(postgres, new FailOnSecondSave());

        var act = () => host.SendAsync(Import(ValidWorkbook()));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(FailOnSecondSave.Message);
        await host.ShouldBeEmptyAsync();
    }

    private static ImportLegacyBook Import(LegacyWorkbook workbook) =>
        new(workbook, @"C:\Users\someone\私人資料夾\2026帳本v1.xlsm", BookName, " Owner@Example.com ", DryRun: false);

    /// <summary>第二次 SaveChanges 前擲出例外，模擬寫到一半失敗（例如連線中斷）。</summary>
    private sealed class FailOnSecondSave : SaveChangesInterceptor
    {
        public const string Message = "模擬寫入中途失敗";
        private int _saves;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            ++_saves == 2 ? throw new InvalidOperationException(Message) : base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private sealed class FixedUser : ICurrentUser
    {
        public string Subject => Actor;
        public string? Email => null;
    }

    /// <summary>與 CLI 相同的組裝方式（Application + Infrastructure），每個測試一個獨立的資料庫。</summary>
    private sealed class Host(ServiceProvider provider, string connectionString) : IAsyncDisposable
    {
        public static async Task<Host> CreateAsync(PostgresFixture postgres, IInterceptor? interceptor = null)
        {
            var connectionString = await postgres.CreateConnectionStringAsync(Ct);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSixJarsApplication(mediatRLicenseKey: null);
            services.AddSixJarsInfrastructure(connectionString);
            services.AddSingleton<ICurrentUser, FixedUser>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
            if (interceptor is not null)
            {
                services.AddSingleton(interceptor);
            }

            return new Host(services.BuildServiceProvider(), connectionString);
        }

        public async Task<ImportLegacyBookResult> SendAsync(ImportLegacyBook request)
        {
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, Ct);
        }

        public SixJarsDbContext Open() =>
            new(new DbContextOptionsBuilder<SixJarsDbContext>().UseNpgsql(connectionString).Options);

        public async Task ShouldBeEmptyAsync()
        {
            await using var db = Open();
            (await db.Books.CountAsync(Ct)).Should().Be(0);
            (await db.Transactions.IgnoreQueryFilters().CountAsync(Ct)).Should().Be(0);
            (await db.PlannedExpenses.IgnoreQueryFilters().CountAsync(Ct)).Should().Be(0);
            (await db.BookMembers.CountAsync(Ct)).Should().Be(0);
            (await db.AuditEntries.CountAsync(Ct)).Should().Be(0);
        }

        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }
}
