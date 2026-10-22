using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.LegacyImport;
using SixJars.Domain.Books;
using SixJars.Infrastructure.Ledger;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.AcceptanceTests;

/// <summary>spec §9 的 M 段驗收：寫入 DB 後，月報各項與 Excel 月工作表、年報年總計與「總表」一致。</summary>
public class ReportAcceptanceTests(LegacyWorkbookFixture fixture, PostgresFixture postgres)
    : IClassFixture<LegacyWorkbookFixture>
{
    [Theory]
    [MemberData(nameof(LegacyWorkbookAcceptanceTests.ImportedMonths), MemberType = typeof(LegacyWorkbookAcceptanceTests))]
    public async Task Monthly_report_matches_excel(int month)
    {
        var ct = TestContext.Current.CancellationToken;
        var workbook = fixture.Require();
        var (createContext, book) = await ImportAsync(workbook, ct);

        await using var readDb = createContext();
        (await MonthFigureComparison.CompareMonthlyReportAsync(
                new SqlLedgerSummaryQuery(readDb), book, workbook.Settings.Year, workbook.Months.Single(m => m.Month == month), ct))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Yearly_report_matches_summary_sheet()
    {
        var ct = TestContext.Current.CancellationToken;
        var workbook = fixture.Require();
        var (createContext, book) = await ImportAsync(workbook, ct);

        await using var readDb = createContext();
        (await MonthFigureComparison.CompareYearlyReportAsync(new SqlLedgerSummaryQuery(readDb), book, workbook, ct))
            .Should().BeEmpty();
    }

    private async Task<(Func<SixJarsDbContext> CreateContext, Book Book)> ImportAsync(
        LegacyWorkbook workbook, CancellationToken ct)
    {
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
        return (createContext, await readDb.Books.AsNoTracking().SingleAsync(b => b.Id == result.Book.Id, ct));
    }
}
