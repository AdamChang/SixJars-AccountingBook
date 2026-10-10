using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.LegacyImport;
using SixJars.Infrastructure.Ledger;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.AcceptanceTests;

/// <summary>spec §9 的 L 段驗收：寫入 DB 後，各浮動主分類的預算 actual 與 Excel「預算」工作表的實際支出欄一致。</summary>
public class BudgetActualAcceptanceTests(LegacyWorkbookFixture fixture, PostgresFixture postgres)
    : IClassFixture<LegacyWorkbookFixture>
{
    [Theory]
    [MemberData(nameof(LegacyWorkbookAcceptanceTests.ImportedMonths), MemberType = typeof(LegacyWorkbookAcceptanceTests))]
    public async Task Budget_actuals_match_excel(int month)
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

        (await MonthFigureComparison.CompareBudgetActualsAsync(
                new SqlLedgerSummaryQuery(readDb), book, workbook.Settings.Year, workbook.Months.Single(m => m.Month == month), ct))
            .Should().BeEmpty();
    }
}
