using FluentAssertions;
using SixJars.Application.LegacyImport;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
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

    private (LegacyPrependMapping Mapping, IReadOnlyList<JunctionLine> Lines) Check(LegacyWorkbook workbook, bool allowAdjustments = false)
    {
        var openingDate = _book.OpeningDate;
        var mapping = LegacyWorkbookMapper.MapInto(workbook, _book);
        return (mapping, LegacyJunction.Check(_book, openingDate, mapping, mapping.Report, allowAdjustments));
    }

    /// <summary>M1：帳本開始年度看最早的歸屬月份；12/31 入帳、歸屬隔年 1 月的交易不會讓年度錯判。</summary>
    [Theory]
    [InlineData("2025-12-30", 202601, 2026)]
    [InlineData("2025-12-30", 202512, 2025)]
    [InlineData("2026-10-18", 202610, 2026)]
    public void Book_start_year_is_the_year_of_the_earliest_budget_month(string openingDate, int earliestBudgetMonth, int expected) =>
        LegacyJunction.BookStartYear(DateOnly.Parse(openingDate), BudgetMonth.FromKey(earliestBudgetMonth)).Should().Be(expected);

    [Theory]
    [InlineData("2025-12-31", 2026)]
    [InlineData("2025-12-30", 2025)]
    public void Book_without_transactions_starts_the_day_after_the_opening_date(string openingDate, int expected) =>
        LegacyJunction.BookStartYear(DateOnly.Parse(openingDate), null).Should().Be(expected);

    [Fact]
    public void Year_right_before_the_book_passes() =>
        LegacyJunction.CheckYear(2026, 2025).Should().BeNull();

    [Theory]
    [InlineData(2024)]
    [InlineData(2026)]
    public void Year_not_right_before_the_book_is_an_error(int excelYear) =>
        LegacyJunction.CheckYear(2026, excelYear).Should().Contain(excelYear.ToString()).And.Contain("2025");

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

    /// <summary>M2：帳本第一天（期初日 + 1）的交易，第一天起每天都會全數計入，餘額不變。</summary>
    [Fact]
    public void Transaction_on_the_first_book_day_passes()
    {
        var (mapping, _) = Check(Workbook2025(date: new DateOnly(2026, 1, 1)));

        mapping.Report.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Transaction_after_the_first_book_day_is_an_error()
    {
        var (mapping, _) = Check(Workbook2025(date: new DateOnly(2026, 1, 2)));

        // spec 處理流程第 4 步：錯誤帶來源的工作表與列號，使用者才能回到 Excel 找到那一列。
        mapping.Report.Errors.Should().ContainSingle(i => i.Sheet == "12月" && i.Row == 7 && i.Message.Contains("2026-01-02"));
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

    /// <summary>spec M3-3：同意調整時，既有項目的差額不是錯誤，改成調整（應有期初 − 推算期末）。</summary>
    [Fact]
    public void Difference_with_adjustments_becomes_an_adjustment()
    {
        var (mapping, lines) = Check(Workbook2025(Settings2025(
            banks: [new("國泰世華銀行", 21691m), new("華南銀行", 11637m), new("國泰投資帳戶", 20m), new("國泰外幣帳戶", 16365m)],
            funds: [new("財務自由帳戶", 9000m), new("外幣現金", 10000m)])), allowAdjustments: true);

        mapping.Report.Errors.Should().BeEmpty();
        lines.Single(l => l.Name == "國泰世華銀行").Should().BeEquivalentTo(new { Difference = (decimal?)1m, Adjustment = -1m });
        lines.Single(l => l.Name == "財務自由帳戶").Adjustment.Should().Be(874.3m);
        lines.Single(l => l.Name == "華南銀行").Adjustment.Should().Be(0m);
    }

    [Fact]
    public void Previous_year_only_account_with_adjustments_is_adjusted_to_zero()
    {
        var (mapping, lines) = Check(Workbook2025(Settings2025(
            banks: [new("國泰世華銀行", 21690m), new("華南銀行", 11637m), new("國泰投資帳戶", 20m), new("國泰外幣帳戶", 16365m), new("郵局", 500m)])),
            allowAdjustments: true);

        mapping.Report.Errors.Should().BeEmpty();
        lines.Single(l => l.Name == "郵局").Adjustment.Should().Be(-500m);
    }

    /// <summary>spec M3-3：設定漏列不是「期初沒接上」，同意調整也仍然是錯誤。</summary>
    [Fact]
    public void Account_only_in_book_with_movement_is_still_an_error_with_adjustments()
    {
        var workbook = new LegacyWorkbook(
            Settings2025(banks: [new("國泰世華銀行", 21690m), new("華南銀行", 11637m), new("國泰投資帳戶", 20m)]), Floating(),
            [Month(3, journal: [
                Row(7, new DateOnly(2025, 3, 5), "國泰世華銀行", "主食", "早餐", -80m),
                Row(8, new DateOnly(2025, 3, 6), "國泰外幣帳戶", "主食", "中餐", -500m)])]);

        var (mapping, lines) = Check(workbook, allowAdjustments: true);

        mapping.Report.Errors.Should().ContainSingle(i => i.Message.Contains("國泰外幣帳戶"));
        lines.Should().OnlyContain(l => l.Adjustment == 0m);
    }

    /// <summary>
    /// spec M3-3：類型不符（D4）同意調整也仍然是錯誤。對應不收集它的期初，
    /// 所以不會被當成「期初沒接上」而調整；其他項目的差額照樣列入調整清單。
    /// </summary>
    [Fact]
    public void Type_mismatch_is_still_an_error_with_adjustments()
    {
        var settings = Settings2025(banks: [new("國泰世華銀行", 21691m), new("國泰投資帳戶", 20m), new("國泰外幣帳戶", 16365m)])
            with { CreditCards = [new("國泰Combo卡", 22668m), new("華南銀行", 11637m)] };

        var (mapping, lines) = Check(Workbook2025(settings), allowAdjustments: true);

        mapping.Report.Errors.Should().ContainSingle(i => i.Message.Contains("華南銀行"));
        lines.Single(l => l.Name == "華南銀行").Adjustment.Should().Be(0m);
        lines.Single(l => l.Name == "國泰世華銀行").Adjustment.Should().Be(-1m);
    }

    /// <summary>spec M3-3：日期晚於帳本第一天（M2）同意調整也仍然是錯誤。</summary>
    [Fact]
    public void Transaction_after_the_first_book_day_is_still_an_error_with_adjustments()
    {
        var settings = Settings2025(banks: [new("國泰世華銀行", 21691m), new("華南銀行", 11637m), new("國泰投資帳戶", 20m), new("國泰外幣帳戶", 16365m)]);

        var (mapping, _) = Check(Workbook2025(settings, date: new DateOnly(2026, 1, 2)), allowAdjustments: true);

        mapping.Report.Errors.Should().ContainSingle(i => i.Sheet == "12月" && i.Row == 7 && i.Message.Contains("2026-01-02"));
    }

    [Fact]
    public void Without_adjustments_lines_carry_no_adjustment()
    {
        var (mapping, lines) = Check(Workbook2025(Settings2025(
            banks: [new("國泰世華銀行", 21691m), new("華南銀行", 11637m), new("國泰投資帳戶", 20m), new("國泰外幣帳戶", 16365m)])));

        mapping.Report.Errors.Should().ContainSingle();
        lines.Should().OnlyContain(l => l.Adjustment == 0m);
    }
}
