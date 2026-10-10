using ClosedXML.Excel;
using FluentAssertions;
using SixJars.Application.LegacyImport;
using SixJars.Infrastructure.LegacyExcel;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Infrastructure.Tests.LegacyExcel;

public class ExcelLegacyWorkbookReaderTests
{
    private static async Task<LegacyWorkbook> ReadAsync()
    {
        Assert.SkipUnless(File.Exists(RepoPaths.LegacyWorkbook), $"找不到 {RepoPaths.LegacyWorkbook}，略過");
        await using var stream = RepoPaths.OpenLegacyWorkbook();
        return await new ExcelLegacyWorkbookReader().ReadAsync(stream, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Reads_settings_and_openings()
    {
        var settings = (await ReadAsync()).Settings;

        settings.Year.Should().Be(2026);
        settings.HandCashOpening.Should().Be(2071m);
        settings.Banks.Should().Contain(new LegacyNamedAmount("國泰世華銀行", 21610m));
        settings.CreditCards.Should().Contain(new LegacyNamedAmount("國泰Combo卡", 22668m));
        settings.PlanningFunds.Should().Contain(new LegacyNamedAmount("財務自由帳戶", 9874.3m));
        settings.Loans.Should().Contain(new LegacyNamedAmount("房屋貸款", 2658876m));
        settings.FixedExpenseItems.Should().Contain("行動電話費");
    }

    [Fact]
    public async Task Reads_january_journal()
    {
        var journal = (await ReadAsync()).Months.Single(m => m.Month == 1).Journal;

        journal.Should().HaveCount(152);
        journal[0].Should().Be(new LegacyJournalRow(7, new DateOnly(2025, 12, 31), "國泰世華銀行", "工作薪資1", null, 84223m, null));
    }

    [Fact]
    public async Task Reads_templates_and_loan_principal()
    {
        var january = (await ReadAsync()).Months.Single(m => m.Month == 1);

        january.Templates.Should().ContainEquivalentOf(new LegacyTemplateRow(52, LegacyTemplateSection.Fixed, "行動電話費", "國泰世華銀行", -599m, new DateOnly(2026, 1, 16), null));
        january.Templates.Should().Contain(t => t.Section == LegacyTemplateSection.Loan && t.Item == "房屋貸款" && t.Amount == -32503m);
        january.LoanPrincipals.Should().Contain(new LegacyNamedAmount("房屋貸款", -28085m));
    }

    [Fact]
    public async Task Reads_month_figures()
    {
        var figures = (await ReadAsync()).Months.Single(m => m.Month == 1).Figures;

        figures.MonthlyDisposable.Should().Be(-4557m);
        figures.WalletAddBack.Should().Be(562m);
        figures.AvailableCash.Should().Be(1700m);
        figures.BankBalances.Should().Contain(new LegacyNamedAmount("國泰世華銀行", 10773m));
        figures.FundBalances.Should().Contain(new LegacyNamedAmount("財務自由帳戶", 12360.3m));
    }

    [Fact]
    public async Task Reads_floating_categories()
    {
        var floating = (await ReadAsync()).FloatingCategories;

        floating.Single(c => c.Main == "主食").Subs.Should().Equal("早餐", "中餐", "晚餐", "宵夜");
    }

    [Fact]
    public async Task Reads_budget_sheet_actuals_for_each_floating_main_category()
    {
        var workbook = await ReadAsync();

        foreach (var month in workbook.Months.Where(m => m.Month <= 3))
        {
            month.Figures.FloatingActuals.Select(a => a.Name)
                .Should().Equal(workbook.FloatingCategories.Select(c => c.Main), $"{month.Month} 月的預算表與清單的浮動主分類一一對應");
            month.Figures.FloatingActuals.Should().Contain(a => a.Amount < 0m, "Excel 的實際支出沿用支出符號");
        }
    }

    /// <summary>「預算」工作表只供驗收比對（L9），缺表時匯入仍要能讀；不需要 reference/，不會略過。</summary>
    [Fact]
    public async Task Missing_budget_sheet_reads_months_with_empty_floating_actuals()
    {
        using var workbook = new XLWorkbook();
        workbook.AddWorksheet("設定").Cell("J4").Value = 2026;
        workbook.AddWorksheet("清單");
        workbook.AddWorksheet("1月").Cell("J6").Value = 100;
        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var result = await new ExcelLegacyWorkbookReader().ReadAsync(stream, TestContext.Current.CancellationToken);

        result.Months.Should().ContainSingle();
        result.Months[0].Figures.MonthlyDisposable.Should().Be(100m);
        result.Months[0].Figures.FloatingActuals.Should().BeEmpty();
    }
}
