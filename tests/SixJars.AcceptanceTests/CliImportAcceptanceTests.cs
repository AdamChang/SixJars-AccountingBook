using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SixJars.Cli;
using SixJars.Infrastructure.Ledger;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.AcceptanceTests;

/// <summary>
/// spec §6 的真實資料驗收，走正式的搬家流程：<c>import-legacy</c>（CLI）寫入 DB 後，SQL 彙總的 1–3 月數字與 Excel 一致。
/// 比對的指標與已知差異都與 <see cref="SqlLedgerAcceptanceTests"/> 相同；<c>reference/</c> 不存在時略過。
/// </summary>
public class CliImportAcceptanceTests(LegacyWorkbookFixture fixture, PostgresFixture postgres)
    : IClassFixture<LegacyWorkbookFixture>
{
    private const string BookName = "驗收帳本";

    [Theory]
    [MemberData(nameof(LegacyWorkbookAcceptanceTests.ImportedMonths), MemberType = typeof(LegacyWorkbookAcceptanceTests))]
    public async Task Imported_book_summary_matches_excel(int month)
    {
        var ct = TestContext.Current.CancellationToken;
        var workbook = fixture.Require();
        var connectionString = await postgres.CreateConnectionStringAsync(ct);
        var output = new StringWriter();

        var exitCode = await CliApp.RunAsync(
            ["import-legacy", "--file", RepoPaths.LegacyWorkbook, "--book-name", BookName, "--owner-email", "owner@example.com"],
            new Dictionary<string, string?> { [CliApp.ConnectionStringVariable] = connectionString },
            output,
            ct);

        exitCode.Should().Be(0, output.ToString());
        await using var db = new SixJarsDbContext(
            new DbContextOptionsBuilder<SixJarsDbContext>().UseNpgsql(connectionString).Options);
        var book = await db.Books.AsNoTracking().SingleAsync(b => b.Name == BookName, ct);
        (await MonthFigureComparison.CompareSqlAsync(
                new SqlLedgerSummaryQuery(db), book, workbook.Settings.Year, workbook.Months.Single(m => m.Month == month), ct))
            .Should().BeEmpty();
    }
}
