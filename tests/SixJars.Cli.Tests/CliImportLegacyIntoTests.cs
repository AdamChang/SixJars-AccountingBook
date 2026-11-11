using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SixJars.Api.Tests;
using SixJars.Application.LegacyImport;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;
using static SixJars.Application.Tests.LegacyImport.LegacyWorkbookFactory;

namespace SixJars.Cli.Tests;

/// <summary>
/// <c>import-legacy --into</c>（補匯舊年度，spec R2-Q4）。Excel 讀取換成假 reader：
/// 依檔名回傳合成的記帳本，邏輯正確性不依賴個資檔案（R3-Q5）。
/// </summary>
public class CliImportLegacyIntoTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 1, 2, 3, TimeSpan.Zero);
    private static readonly DateOnly Jan6 = new(2026, 1, 6);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Dictionary<string, LegacyWorkbook> Workbooks = new()
    {
        ["2026帳本v3.xlsm"] = Workbook(Month(1, journal: [Row(7, Jan6, "國泰世華銀行", "工作薪資1", null, 84223m)])),
        // 國泰世華銀行 21690 − 80 = 2026 的期初 21610；郵局只在 2025 出現、期末 0。
        ["2025記帳.xlsm"] = Workbook2025(bankOpening: 21690m),
        ["2025差1元.xlsm"] = Workbook2025(bankOpening: 21691m),
    };

    private static LegacyWorkbook Workbook2025(decimal bankOpening) => new(
        Settings() with
        {
            Year = 2025,
            Banks = [new("國泰世華銀行", bankOpening), new("華南銀行", 11637m), new("國泰投資帳戶", 20m), new("國泰外幣帳戶", 16365m), new("郵局", 0m)],
        },
        Floating(),
        [Month(3, journal: [Row(7, new DateOnly(2025, 3, 5), "國泰世華銀行", "主食", "早餐", -80m)])]);

    [Theory]
    [InlineData("--book-name", "我的帳本")]
    [InlineData("--owner-email", "owner@example.com")]
    public async Task Into_mixed_with_new_book_options_is_rejected(string option, string value)
    {
        var output = new StringWriter();

        var exitCode = await RunAsync("Host=unused", output,
            "import-legacy", "--file", await FileAsync("2025記帳.xlsm"), "--into", Guid.NewGuid().ToString(), option, value);

        exitCode.Should().Be(1);
        output.ToString().Should().Contain("--into").And.Contain("--book-name");
    }

    [Theory]
    [InlineData]
    [InlineData("--book-name", "我的帳本")]
    public async Task Neither_mode_complete_is_rejected(params string[] extra)
    {
        var output = new StringWriter();

        var exitCode = await RunAsync("Host=unused", output, ["import-legacy", "--file", await FileAsync("2025記帳.xlsm"), .. extra]);

        exitCode.Should().Be(1);
        output.ToString().Should().Contain("--into").And.Contain("--owner-email");
    }

    [Fact]
    public async Task Into_missing_book_fails_with_clear_message()
    {
        var connectionString = await postgres.CreateConnectionStringAsync(Ct);
        var missing = Guid.NewGuid();
        var output = new StringWriter();

        var exitCode = await RunAsync(connectionString, output, "import-legacy", "--file", await FileAsync("2025記帳.xlsm"), "--into", missing.ToString());

        exitCode.Should().Be(1);
        output.ToString().Should().Contain(missing.ToString()).And.Contain("找不到帳本").And.NotContain("   at ");
    }

    [Fact]
    public async Task Into_dry_run_prints_junction_summary_and_writes_nothing()
    {
        var connectionString = await postgres.CreateConnectionStringAsync(Ct);
        var bookId = await ImportBookAsync(connectionString);
        var output = new StringWriter();

        var exitCode = await RunAsync(connectionString, output,
            "import-legacy", "--file", await FileAsync("2025記帳.xlsm"), "--into", bookId.ToString(), "--dry-run");

        exitCode.Should().Be(0, output.ToString());
        var text = output.ToString();
        text.Should().Contain("期初日：2026-01-05 → 2025-03-04");
        text.Should().Contain("國泰世華銀行").And.Contain("21,690").And.Contain("-80").And.Contain("21,610");
        text.Should().Contain("郵局").And.Contain("dry run").And.Contain("新增並封存帳戶 1 個");
        await using var db = Open(connectionString);
        (await db.Books.SingleAsync(Ct)).OpeningDate.Should().Be(new DateOnly(2026, 1, 5));
        (await db.Transactions.CountAsync(Ct)).Should().Be(1);
    }

    [Fact]
    public async Task Into_writes_the_previous_year()
    {
        var connectionString = await postgres.CreateConnectionStringAsync(Ct);
        var bookId = await ImportBookAsync(connectionString);
        var output = new StringWriter();

        var exitCode = await RunAsync(connectionString, output, "import-legacy", "--file", await FileAsync("2025記帳.xlsm"), "--into", bookId.ToString());

        exitCode.Should().Be(0, output.ToString());
        output.ToString().Should().Contain($"已把 2025記帳.xlsm 補匯到帳本 {bookId}");
        await using var db = Open(connectionString);
        (await db.Books.SingleAsync(Ct)).OpeningDate.Should().Be(new DateOnly(2025, 3, 4));
        (await db.Transactions.CountAsync(Ct)).Should().Be(2);
    }

    [Fact]
    public async Task Into_junction_error_prints_difference_without_row_zero_and_writes_nothing()
    {
        var connectionString = await postgres.CreateConnectionStringAsync(Ct);
        var bookId = await ImportBookAsync(connectionString);
        var output = new StringWriter();

        var exitCode = await RunAsync(connectionString, output, "import-legacy", "--file", await FileAsync("2025差1元.xlsm"), "--into", bookId.ToString());

        exitCode.Should().Be(1);
        var text = output.ToString();
        text.Should().Contain("[錯誤] 銜接檢查：").And.Contain("國泰世華銀行").And.NotContain("第 0 列");
        text.Should().Contain("沒有寫入任何資料");
        await using var db = Open(connectionString);
        (await db.Books.SingleAsync(Ct)).OpeningDate.Should().Be(new DateOnly(2026, 1, 5));
    }

    /// <summary>spec M3-2：--adjust 才把差額轉成餘額調整，摘要多一欄調整金額。</summary>
    [Fact]
    public async Task Into_with_adjust_fills_the_difference_with_a_balance_adjustment()
    {
        var connectionString = await postgres.CreateConnectionStringAsync(Ct);
        var bookId = await ImportBookAsync(connectionString);
        var output = new StringWriter();

        var exitCode = await RunAsync(connectionString, output,
            "import-legacy", "--file", await FileAsync("2025差1元.xlsm"), "--into", bookId.ToString(), "--adjust");

        exitCode.Should().Be(0, output.ToString());
        var text = output.ToString();
        text.Should().Contain("調整").And.Contain("餘額調整 1 筆").And.NotContain("[錯誤]");
        text.Should().MatchRegex("國泰世華銀行｜.*｜1｜-1");
        await using var db = Open(connectionString);
        (await db.Transactions.CountAsync(t => t.Kind == Domain.Transactions.TransactionKind.BalanceAdjustment, Ct)).Should().Be(1);
    }

    [Fact]
    public async Task Adjust_without_into_is_rejected()
    {
        var output = new StringWriter();

        var exitCode = await RunAsync("Host=unused", output,
            "import-legacy", "--file", await FileAsync("2026帳本v3.xlsm"), "--book-name", "我的帳本", "--owner-email", "owner@example.com", "--adjust");

        exitCode.Should().Be(1);
        output.ToString().Should().Contain("--adjust").And.Contain("--into");
    }

    /// <summary>用既有模式匯入 2026 合成帳本，回傳帳本 Id（從輸出「已匯入帳本 {id}」取出）。</summary>
    private async Task<Guid> ImportBookAsync(string connectionString)
    {
        var output = new StringWriter();
        var exitCode = await RunAsync(connectionString, output,
            "import-legacy", "--file", await FileAsync("2026帳本v3.xlsm"), "--book-name", "我的帳本", "--owner-email", "owner@example.com");
        exitCode.Should().Be(0, output.ToString());
        await using var db = Open(connectionString);
        return (await db.Books.SingleAsync(Ct)).Id.Value;
    }

    /// <summary>CLI 會先檢查檔案存在，所以建立一個空檔；內容由假 reader 依檔名決定。</summary>
    private static async Task<string> FileAsync(string name)
    {
        var directory = Directory.CreateTempSubdirectory("sixjars-legacy-");
        var file = Path.Combine(directory.FullName, name);
        await File.WriteAllBytesAsync(file, [], Ct);
        return file;
    }

    private static Task<int> RunAsync(string connectionString, TextWriter output, params string[] args) =>
        CliApp.RunAsync(
            args,
            new Dictionary<string, string?> { [CliApp.ConnectionStringVariable] = connectionString },
            output,
            Ct,
            services =>
            {
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
                services.AddSingleton<ILegacyWorkbookReader, FakeReader>();
            });

    private static SixJarsDbContext Open(string connectionString) =>
        new(new DbContextOptionsBuilder<SixJarsDbContext>().UseNpgsql(connectionString).Options);

    private sealed class FakeReader : ILegacyWorkbookReader
    {
        public Task<LegacyWorkbook> ReadAsync(Stream stream, CancellationToken cancellationToken) =>
            Task.FromResult(Workbooks[Path.GetFileName(((FileStream)stream).Name)]);
    }
}
