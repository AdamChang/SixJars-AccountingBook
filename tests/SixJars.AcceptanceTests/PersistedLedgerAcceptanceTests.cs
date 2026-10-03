using FluentAssertions;
using SixJars.Application.LegacyImport;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.AcceptanceTests;

public class PersistedLedgerAcceptanceTests(LegacyWorkbookFixture fixture, PostgresFixture postgres)
    : IClassFixture<LegacyWorkbookFixture>
{
    [Theory]
    [MemberData(nameof(LegacyWorkbookAcceptanceTests.ImportedMonths), MemberType = typeof(LegacyWorkbookAcceptanceTests))]
    public async Task Persisted_ledger_still_matches_excel(int month)
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
        var ledger = await new LedgerSnapshotLoader(readDb).LoadAsync(result.Book.Id, ct);

        MonthFigureComparison.Compare(ledger, workbook.Settings.Year, workbook.Months.Single(m => m.Month == month))
            .Should().BeEmpty();
    }
}
