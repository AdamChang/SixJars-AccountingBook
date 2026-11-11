using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SixJars.Application.Auditing;
using SixJars.Application.LegacyImport;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Ledger;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;
using static SixJars.Application.Tests.LegacyImport.LegacyWorkbookFactory;

namespace SixJars.Infrastructure.Tests.LegacyExcel;

/// <summary>補匯舊年度（spec D1、D2、D7、D8、R3-Q2、R3-Q4）：先匯入 2026 合成帳本，再把 2025 接到前面。</summary>
public class PrependLegacyYearTests(PostgresFixture postgres)
{
    private static readonly DateOnly Jan6 = new(2026, 1, 6);

    /// <summary>2026 帳本的期初日：最早的交易是 1/6。</summary>
    private static readonly DateOnly BookOpening = new(2026, 1, 5);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>2026：薪資、中餐、已付的電話費（同時產生一筆預定支出）。</summary>
    private static LegacyWorkbook Workbook2026() => Workbook(Month(1,
        journal: [Row(7, Jan6, "國泰世華銀行", "工作薪資1", null, 84223m), Row(8, Jan6, "現金", "主食", "中餐", -80m)],
        templates: [new(52, LegacyTemplateSection.Fixed, "行動電話費", "國泰世華銀行", -599m, new DateOnly(2026, 1, 16), null)]));

    /// <summary>
    /// 與 2026 銜接得上的 2025：國泰世華銀行 22289 − 80（點心）− 599（電話費）= 21610。
    /// 郵局與「點心」只在 2025 出現，郵局期末為 0。
    /// </summary>
    private static LegacyWorkbook Workbook2025(
        int year = 2025,
        decimal bankOpening = 22289m,
        DateOnly? snackDate = null,
        IReadOnlyList<LegacyNamedAmount>? cards = null) =>
        new(Settings() with
            {
                Year = year,
                Banks = [new("國泰世華銀行", bankOpening), new("華南銀行", 11637m), new("國泰投資帳戶", 20m), new("國泰外幣帳戶", 16365m), new("郵局", 0m)],
                CreditCards = cards ?? [new("國泰Combo卡", 22668m)],
            },
            [new("主食", ["早餐", "中餐", "晚餐", "宵夜", "點心"]), new("金融交易", ["手續費"])],
            [Month(snackDate is null ? 3 : 12,
                journal: [Row(7, snackDate ?? new DateOnly(2025, 3, 5), "國泰世華銀行", "主食", "點心", -80m)],
                templates: [new(52, LegacyTemplateSection.Fixed, "行動電話費", "國泰世華銀行", -599m, new DateOnly(2025, 3, 16), null)])]);

    private static PrependLegacyYear Prepend(Guid bookId, LegacyWorkbook workbook, bool dryRun = false) =>
        new(workbook, @"C:\Users\someone\私人資料夾\2025記帳.xlsm", bookId, dryRun);

    [Fact]
    public async Task Successful_prepend_moves_openings_and_keeps_every_later_balance()
    {
        await using var host = await LegacyImportHost.CreateAsync(postgres);
        var bookId = await ImportBookAsync(host);
        var before = await DailyBalancesAsync(host, bookId);

        var result = await host.SendAsync(Prepend(bookId, Workbook2025()));

        result.Report.Errors.Should().BeEmpty();
        result.Written.Should().BeTrue();
        result.PreviousOpeningDate.Should().Be(BookOpening);
        result.NewOpeningDate.Should().Be(new DateOnly(2025, 3, 4));
        result.Transactions.Should().Be(2);
        result.PlannedExpenses.Should().Be(1);
        result.Junction.Should().ContainSingle(l => l.Name == "國泰世華銀行").Which.Difference.Should().Be(0m);

        await using var db = host.Open();
        var book = await db.Books.SingleAsync(Ct);
        book.OpeningDate.Should().Be(new DateOnly(2025, 3, 4));
        book.FindAccount("國泰世華銀行")!.OpeningBalance.Should().Be(22289m);
        (await db.Transactions.CountAsync(Ct)).Should().Be(5);
        (await db.PlannedExpenses.CountAsync(Ct)).Should().Be(2);
        (await DailyBalancesAsync(host, bookId, before.Keys)).Should().Equal(before);
    }

    [Fact]
    public async Task Settings_only_in_previous_year_are_archived_and_existing_settings_are_untouched()
    {
        await using var host = await LegacyImportHost.CreateAsync(postgres);
        var bookId = await ImportBookAsync(host);
        var existing = await SettingsAsync(host);

        var result = await host.SendAsync(Prepend(bookId, Workbook2025()));

        result.Written.Should().BeTrue();
        result.ArchivedAccounts.Should().Be(1);
        result.ArchivedPlanningFunds.Should().Be(0);
        result.ArchivedCategories.Should().Be(1);
        await using var db = host.Open();
        var book = await db.Books.SingleAsync(Ct);
        book.FindAccount("郵局")!.IsArchived.Should().BeTrue();
        book.FindCategory("主食", "點心")!.IsArchived.Should().BeTrue();
        (await SettingsAsync(host)).Where(s => !s.StartsWith("Account:郵局:") && !s.StartsWith("Category:點心:")).Should().Equal(existing);
    }

    [Fact]
    public async Task Year_not_right_before_the_book_writes_nothing()
    {
        await using var host = await LegacyImportHost.CreateAsync(postgres);
        var bookId = await ImportBookAsync(host);
        var before = await StateAsync(host);

        var result = await host.SendAsync(Prepend(bookId, Workbook2025(year: 2024)));

        result.Written.Should().BeFalse();
        result.Report.Errors.Should().ContainSingle(i => i.Sheet == LegacyJunction.Sheet && i.Message.Contains("2024"));
        result.Junction.Should().BeEmpty();
        (await StateAsync(host)).Should().BeEquivalentTo(before, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task One_dollar_difference_writes_nothing()
    {
        await using var host = await LegacyImportHost.CreateAsync(postgres);
        var bookId = await ImportBookAsync(host);
        var before = await StateAsync(host);

        var result = await host.SendAsync(Prepend(bookId, Workbook2025(bankOpening: 22290m)));

        result.Written.Should().BeFalse();
        result.Report.Errors.Should().ContainSingle(i => i.Message.Contains("國泰世華銀行"));
        result.Junction.Single(l => l.Name == "國泰世華銀行").Difference.Should().Be(1m);
        (await StateAsync(host)).Should().BeEquivalentTo(before, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Transaction_after_the_book_opening_date_writes_nothing()
    {
        await using var host = await LegacyImportHost.CreateAsync(postgres);
        var bookId = await ImportBookAsync(host);
        var before = await StateAsync(host);

        var result = await host.SendAsync(Prepend(bookId, Workbook2025(snackDate: Jan6)));

        result.Written.Should().BeFalse();
        result.Report.Errors.Should().Contain(i => i.Message.Contains("2026-01-06"));
        (await StateAsync(host)).Should().BeEquivalentTo(before, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Same_name_with_different_account_type_writes_nothing()
    {
        await using var host = await LegacyImportHost.CreateAsync(postgres);
        var bookId = await ImportBookAsync(host);
        var before = await StateAsync(host);

        var result = await host.SendAsync(Prepend(bookId, Workbook2025(cards: [new("國泰Combo卡", 22668m), new("華南銀行", 0m)])));

        result.Written.Should().BeFalse();
        result.Report.Errors.Should().Contain(i => i.Message.Contains("華南銀行"));
        (await StateAsync(host)).Should().BeEquivalentTo(before, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Lock_date_is_bypassed_kept_and_warned()
    {
        await using var host = await LegacyImportHost.CreateAsync(postgres);
        var bookId = await ImportBookAsync(host);
        var lockDate = new DateOnly(2026, 1, 31);
        await using (var db = host.Open())
        {
            (await db.Books.SingleAsync(Ct)).SetLockDate(lockDate);
            await db.SaveChangesAsync(Ct);
        }

        var result = await host.SendAsync(Prepend(bookId, Workbook2025()));

        result.Written.Should().BeTrue();
        result.Report.Warnings.Should().ContainSingle(i => i.Sheet == LegacyJunction.Sheet && i.Message.Contains("鎖帳"));
        await using var check = host.Open();
        (await check.Books.SingleAsync(Ct)).LockDate.Should().Be(lockDate);
    }

    [Fact]
    public async Task Dry_run_reports_the_same_as_a_real_run_and_writes_nothing()
    {
        await using var host = await LegacyImportHost.CreateAsync(postgres);
        var bookId = await ImportBookAsync(host);
        var before = await StateAsync(host);

        var dryRun = await host.SendAsync(Prepend(bookId, Workbook2025(), dryRun: true));

        dryRun.Written.Should().BeFalse();
        (await StateAsync(host)).Should().BeEquivalentTo(before, o => o.WithStrictOrdering());

        var real = await host.SendAsync(Prepend(bookId, Workbook2025()));

        dryRun.Should().BeEquivalentTo(real, o => o.Excluding(r => r.Written).WithStrictOrdering());
    }

    [Fact]
    public async Task Success_writes_exactly_an_import_and_an_update_audit_entry()
    {
        await using var host = await LegacyImportHost.CreateAsync(postgres);
        var bookId = await ImportBookAsync(host);

        await host.SendAsync(Prepend(bookId, Workbook2025()));

        await using var db = host.Open();
        var all = await db.AuditEntries.ToListAsync(Ct);
        all.Should().HaveCount(3, "2026 匯入一筆，補匯兩筆");
        var entries = all.Where(e => e.After!.Contains("previousOpeningDate") || e.Action == AuditAction.Update).ToList();

        var import = entries.Should().ContainSingle(e => e.Action == AuditAction.Import).Which;
        import.EntityType.Should().Be(AuditEntityTypes.Book);
        import.EntityId.Should().Be(bookId);
        import.Before.Should().BeNull();
        using (var after = JsonDocument.Parse(import.After!))
        {
            after.RootElement.GetProperty("fileName").GetString().Should().Be("2025記帳.xlsm");
            after.RootElement.GetProperty("transactions").GetInt32().Should().Be(2);
            after.RootElement.GetProperty("plannedExpenses").GetInt32().Should().Be(1);
            after.RootElement.GetProperty("previousOpeningDate").GetString().Should().Be("2026-01-05");
        }

        var update = entries.Should().ContainSingle(e => e.Action == AuditAction.Update).Which;
        update.EntityType.Should().Be(AuditEntityTypes.Book);
        update.EntityId.Should().Be(bookId);
        ShouldBeOpenings(update.Before!, "2026-01-05", 21610m);
        ShouldBeOpenings(update.After!, "2025-03-04", 22289m);
    }

    [Fact]
    public async Task Book_changed_after_loading_is_a_concurrency_conflict()
    {
        var interceptor = new ChangeBookBeforeSave();
        await using var host = await LegacyImportHost.CreateAsync(postgres, interceptor);
        var bookId = await ImportBookAsync(host);
        interceptor.ConnectionString = host.ConnectionString;

        var act = () => host.SendAsync(Prepend(bookId, Workbook2025()));

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await using var db = host.Open();
        (await db.Books.SingleAsync(Ct)).OpeningDate.Should().Be(BookOpening);
        (await db.Transactions.CountAsync(Ct)).Should().Be(3);
    }

    /// <summary>Update 稽核只列有變動的項目：只有國泰世華銀行的期初改了。</summary>
    private static void ShouldBeOpenings(string json, string openingDate, decimal bankOpening)
    {
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("openingDate").GetString().Should().Be(openingDate);
        var account = doc.RootElement.GetProperty("accounts").EnumerateArray().Should().ContainSingle().Subject;
        account.GetProperty("name").GetString().Should().Be("國泰世華銀行");
        account.GetProperty("openingBalance").GetDecimal().Should().Be(bankOpening);
        doc.RootElement.GetProperty("planningFunds").GetArrayLength().Should().Be(0);
    }

    private static async Task<Guid> ImportBookAsync(LegacyImportHost host)
    {
        var result = await host.SendAsync(new ImportLegacyBook(Workbook2026(), "2026帳本v3.xlsm", "我們家的帳本", "owner@example.com", DryRun: false));
        result.Report.Errors.Should().BeEmpty();
        return result.BookId!.Value;
    }

    /// <summary>2026 每一天（期初日隔天到月底）每個帳戶與財務規劃帳戶的餘額。</summary>
    private static async Task<Dictionary<string, decimal>> DailyBalancesAsync(LegacyImportHost host, Guid bookId, IEnumerable<string>? keys = null)
    {
        await using var db = host.Open();
        var ledger = await new LedgerSnapshotLoader(db).LoadAsync(new BookId(bookId), Ct);
        var calculator = new BalanceCalculator(ledger);
        var balances = new Dictionary<string, decimal>();
        for (var date = BookOpening.AddDays(1); date <= new DateOnly(2026, 1, 31); date = date.AddDays(1))
        {
            foreach (var account in ledger.Book.Accounts)
            {
                balances[$"{account.Id}@{date}"] = calculator.AccountBalanceAsOf(account.Id, date);
            }

            foreach (var fund in ledger.Book.PlanningFunds)
            {
                balances[$"{fund.Id}@{date}"] = calculator.FundBalanceAsOf(fund.Id, date);
            }
        }

        return keys is null ? balances : keys.ToDictionary(k => k, k => balances[k]);
    }

    /// <summary>設定項目的名稱、排序、類型／性質與封存狀態（依建立順序排列）。</summary>
    private static async Task<List<string>> SettingsAsync(LegacyImportHost host)
    {
        await using var db = host.Open();
        var book = await db.Books.SingleAsync(Ct);
        return
        [
            .. book.Accounts.OrderBy(a => a.Id.Value).Select(a => $"Account:{a.Name}:{a.SortOrder}:{a.Type}:{a.IsArchived}"),
            .. book.PlanningFunds.OrderBy(f => f.Id.Value).Select(f => $"Fund:{f.Name}:{f.SortOrder}:{f.IsArchived}"),
            .. book.Categories.OrderBy(c => c.Id.Value).Select(c => $"Category:{c.Name}:{c.SortOrder}:{c.Nature}:{c.IsArchived}"),
        ];
    }

    private sealed record State(
        DateOnly OpeningDate, DateOnly? LockDate, List<string> Settings, List<decimal> AccountOpenings,
        int Transactions, int PlannedExpenses, int AuditEntries);

    private static async Task<State> StateAsync(LegacyImportHost host)
    {
        await using var db = host.Open();
        var book = await db.Books.SingleAsync(Ct);
        return new State(
            book.OpeningDate, book.LockDate, await SettingsAsync(host), [.. book.Accounts.Select(a => a.OpeningBalance)],
            await db.Transactions.IgnoreQueryFilters().CountAsync(Ct),
            await db.PlannedExpenses.IgnoreQueryFilters().CountAsync(Ct),
            await db.AuditEntries.CountAsync(Ct));
    }

    /// <summary>第一次 SaveChanges 前，另一個連線先改了帳本（模擬 Web 同時在改設定）。</summary>
    private sealed class ChangeBookBeforeSave : SaveChangesInterceptor
    {
        private bool _done;

        public string? ConnectionString { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (ConnectionString is not null && !_done)
            {
                _done = true;
                await using var other = LegacyImportHost.Open(ConnectionString);
                (await other.Books.SingleAsync(cancellationToken)).SetLockDate(new DateOnly(2026, 1, 31));
                await other.SaveChangesAsync(cancellationToken);
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
