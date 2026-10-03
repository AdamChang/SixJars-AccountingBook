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
}
