using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.LegacyImport;
using SixJars.Infrastructure.Ledger;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.AcceptanceTests;

/// <summary>spec §6 的真實資料驗收：寫入 DB 後，SQL 彙總的 1–3 月數字與 Excel 一致（比對的指標與已知差異都與 P1 相同）。</summary>
public class SqlLedgerAcceptanceTests(LegacyWorkbookFixture fixture, PostgresFixture postgres)
    : IClassFixture<LegacyWorkbookFixture>
{
    [Theory]
    [MemberData(nameof(LegacyWorkbookAcceptanceTests.ImportedMonths), MemberType = typeof(LegacyWorkbookAcceptanceTests))]
    public async Task Sql_summary_matches_excel(int month)
    {
        var ct = TestContext.Current.CancellationToken;
        var workbook = fixture.Require();
        var result = LegacyWorkbookMapper.Map(workbook);
        var createContext = await postgres.CreateDatabaseAsync(ct);
        await using (var db = createContext())
        {
            db.Books.Add(result.Book);
            db.Transactions.AddRange(result.Transactions);
            db.PlannedExpenses.AddRange(result.PlannedExpenses);
            await db.SaveChangesAsync(ct);
        }

        await using var readDb = createContext();
        var book = await readDb.Books.AsNoTracking().SingleAsync(b => b.Id == result.Book.Id, ct);

        (await MonthFigureComparison.CompareSqlAsync(
                new SqlLedgerSummaryQuery(readDb), book, workbook.Settings.Year, workbook.Months.Single(m => m.Month == month), ct))
            .Should().BeEmpty();
    }
}
