using FluentAssertions;
using SixJars.Application.LegacyImport;
using SixJars.Domain.Ledger;
using Xunit;

namespace SixJars.AcceptanceTests;

public class LegacyWorkbookAcceptanceTests(LegacyWorkbookFixture fixture) : IClassFixture<LegacyWorkbookFixture>
{
    public static TheoryData<int> ImportedMonths => [1, 2, 3];

    [Fact]
    public void Import_has_no_errors()
    {
        var report = LegacyWorkbookMapper.Map(fixture.Require()).Report;

        foreach (var issue in report.Corrections.Concat(report.Warnings))
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"{issue.Sheet} 第 {issue.Row} 列：{issue.Message}");
        }

        report.Errors.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(ImportedMonths))]
    public void Monthly_figures_match_excel(int month)
    {
        var workbook = fixture.Require();
        var result = LegacyWorkbookMapper.Map(workbook);
        var ledger = new LedgerSnapshot(result.Book, result.Transactions, result.PlannedExpenses);

        MonthFigureComparison.Compare(ledger, workbook.Settings.Year, workbook.Months.Single(m => m.Month == month))
            .Should().BeEmpty();
    }
}
