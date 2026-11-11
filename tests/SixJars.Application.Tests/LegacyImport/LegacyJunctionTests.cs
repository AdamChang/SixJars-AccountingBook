using FluentAssertions;
using SixJars.Application.LegacyImport;
using SixJars.Domain.Books;
using Xunit;
using static SixJars.Application.Tests.LegacyImport.LegacyWorkbookFactory;

namespace SixJars.Application.Tests.LegacyImport;

/// <summary>補匯舊年度的年度、日期與銜接檢查（spec R2-Q1、R2-Q2、R2-Q5、R3-Q1、D5）。</summary>
public class LegacyJunctionTests
{
    private const string JunctionSheet = "銜接檢查";

    /// <summary>2026 合成帳本：沒有任何交易，期初日 2025-12-31。</summary>
    private readonly Book _book = LegacyWorkbookMapper.Map(Workbook()).Book;

    /// <summary>與 2026 銜接得上的 2025 設定：國泰世華銀行 21690 − 80（3/5 早餐）= 21610。</summary>
    private static LegacySettings Settings2025(
        IReadOnlyList<LegacyNamedAmount>? banks = null,
        IReadOnlyList<LegacyNamedAmount>? funds = null) =>
        Settings(funds: funds) with
        {
            Year = 2025,
            Banks = banks ?? [new("國泰世華銀行", 21690m), new("華南銀行", 11637m), new("國泰投資帳戶", 20m), new("國泰外幣帳戶", 16365m)],
        };

    /// <param name="date">指定時放在 12 月表（1 月表裡的 2026-01-01 會被 LegacyDateNormalizer 修正成 2025）。</param>
    private static LegacyWorkbook Workbook2025(LegacySettings? settings = null, DateOnly? date = null) =>
        new(settings ?? Settings2025(), Floating(),
            [Month(date is null ? 3 : 12, journal: [Row(7, date ?? new DateOnly(2025, 3, 5), "國泰世華銀行", "主食", "早餐", -80m)])]);

    private (LegacyPrependMapping Mapping, IReadOnlyList<JunctionLine> Lines) Check(LegacyWorkbook workbook)
    {
        var openingDate = _book.OpeningDate;
        var mapping = LegacyWorkbookMapper.MapInto(workbook, _book);
        return (mapping, LegacyJunction.Check(_book, openingDate, mapping, mapping.Report));
    }

    [Theory]
    [InlineData("2025-12-31", 2025)]
    [InlineData("2026-10-18", 2025)]
    public void Year_right_before_the_book_passes(string openingDate, int excelYear) =>
        LegacyJunction.CheckYear(DateOnly.Parse(openingDate), excelYear).Should().BeNull();

    [Theory]
    [InlineData("2025-12-31", 2024)]
    [InlineData("2025-12-31", 2026)]
    [InlineData("2026-10-18", 2026)]
    public void Year_not_right_before_the_book_is_an_error(string openingDate, int excelYear) =>
        LegacyJunction.CheckYear(DateOnly.Parse(openingDate), excelYear).Should().Contain(excelYear.ToString());

    [Fact]
    public void Matching_junction_has_no_errors_and_summarises_each_account()
    {
        var (mapping, lines) = Check(Workbook2025());

        mapping.Report.Errors.Should().BeEmpty();
        var bank = lines.Single(l => l.Name == "國泰世華銀行");
        bank.Should().BeEquivalentTo(new { IsPlanningFund = false, PreviousOpening = 21690m, Movement = -80m, ExpectedOpening = 21610m, CurrentOpening = (decimal?)21610m });
        bank.PreviousClosing.Should().Be(21610m);
        bank.Difference.Should().Be(0m);
        lines.Single(l => l.Name == "財務自由帳戶").IsPlanningFund.Should().BeTrue();
    }

    [Fact]
    public void One_dollar_difference_is_an_error_with_the_difference()
    {
        var (mapping, lines) = Check(Workbook2025(Settings2025(
            banks: [new("國泰世華銀行", 21691m), new("華南銀行", 11637m), new("國泰投資帳戶", 20m), new("國泰外幣帳戶", 16365m)])));

        lines.Single(l => l.Name == "國泰世華銀行").Difference.Should().Be(1m);
        mapping.Report.Errors.Should().ContainSingle()
            .Which.Should().Match<ImportIssue>(i => i.Sheet == JunctionSheet && i.Row == 0 && i.Message.Contains("國泰世華銀行") && i.Message.Contains('1'));
    }

    [Fact]
    public void Planning_fund_difference_is_an_error()
    {
        var (mapping, _) = Check(Workbook2025(Settings2025(funds: [new("財務自由帳戶", 9874m), new("外幣現金", 10000m)])));

        mapping.Report.Errors.Should().ContainSingle(i => i.Sheet == JunctionSheet && i.Message.Contains("財務自由帳戶"));
    }

    [Fact]
    public void Transaction_on_the_book_opening_date_passes()
    {
        var (mapping, _) = Check(Workbook2025(date: new DateOnly(2025, 12, 31)));

        mapping.Report.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Transaction_after_the_book_opening_date_is_an_error()
    {
        var (mapping, _) = Check(Workbook2025(date: new DateOnly(2026, 1, 1)));

        // spec 處理流程第 4 步：錯誤帶來源的工作表與列號，使用者才能回到 Excel 找到那一列。
        mapping.Report.Errors.Should().ContainSingle(i => i.Sheet == "12月" && i.Row == 7 && i.Message.Contains("2026-01-01"));
    }

    [Fact]
    public void Account_only_in_book_with_previous_year_movement_is_an_error()
    {
        // 國泰外幣帳戶不在 2025 設定，但 2025 流水帳用了它：期初不改、交易照寫，之後每天的餘額都會少 500。
        var workbook = new LegacyWorkbook(
            Settings2025(banks: [new("國泰世華銀行", 21690m), new("華南銀行", 11637m), new("國泰投資帳戶", 20m)]), Floating(),
            [Month(3, journal: [
                Row(7, new DateOnly(2025, 3, 5), "國泰世華銀行", "主食", "早餐", -80m),
                Row(8, new DateOnly(2025, 3, 6), "國泰外幣帳戶", "主食", "中餐", -500m)])]);

        var (mapping, lines) = Check(workbook);

        lines.Single(l => l.Name == "國泰外幣帳戶").Movement.Should().Be(-500m);
        mapping.Report.Errors.Should().ContainSingle(i => i.Sheet == JunctionSheet && i.Message.Contains("國泰外幣帳戶") && i.Message.Contains("500"));
    }

    [Fact]
    public void Account_only_in_previous_year_with_zero_closing_passes()
    {
        var (mapping, lines) = Check(Workbook2025(Settings2025(
            banks: [new("國泰世華銀行", 21690m), new("華南銀行", 11637m), new("國泰投資帳戶", 20m), new("國泰外幣帳戶", 16365m), new("郵局", 0m)])));

        mapping.Report.Errors.Should().BeEmpty();
        lines.Single(l => l.Name == "郵局").Should().BeEquivalentTo(new { ExpectedOpening = 0m, CurrentOpening = (decimal?)0m, Difference = (decimal?)0m });
    }

    [Fact]
    public void Account_only_in_previous_year_with_nonzero_closing_is_an_error()
    {
        var (mapping, lines) = Check(Workbook2025(Settings2025(
            banks: [new("國泰世華銀行", 21690m), new("華南銀行", 11637m), new("國泰投資帳戶", 20m), new("國泰外幣帳戶", 16365m), new("郵局", 500m)])));

        lines.Single(l => l.Name == "郵局").Difference.Should().Be(500m);
        mapping.Report.Errors.Should().ContainSingle(i => i.Sheet == JunctionSheet && i.Message.Contains("郵局") && i.Message.Contains("500"));
    }

    [Fact]
    public void Account_only_in_book_is_summarised_but_not_checked()
    {
        var (mapping, lines) = Check(Workbook2025(Settings2025(
            banks: [new("國泰世華銀行", 21690m), new("華南銀行", 11637m), new("國泰投資帳戶", 20m)])));

        mapping.Report.Errors.Should().BeEmpty();
        var foreign = lines.Single(l => l.Name == "國泰外幣帳戶");
        foreign.CurrentOpening.Should().BeNull();
        foreign.Difference.Should().BeNull();
        foreign.ExpectedOpening.Should().Be(16365m);
    }
}
