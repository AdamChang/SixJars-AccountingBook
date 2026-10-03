using FluentAssertions;
using SixJars.Application.LegacyImport;
using SixJars.Domain.Books;
using Xunit;
using static SixJars.Application.Tests.LegacyImport.LegacyWorkbookFactory;

namespace SixJars.Application.Tests.LegacyImport;

public class LegacyWorkbookMapperSettingsTests
{
    [Fact]
    public void Creates_hand_cash_counted_and_foreign_cash_not_counted()
    {
        var book = LegacyWorkbookMapper.Map(Workbook()).Book;

        var cash = book.FindAccount("現金")!;
        cash.OpeningBalance.Should().Be(2071m);
        cash.CountsAsAvailableCash.Should().BeTrue();
        var foreignCash = book.FindAccount("外幣現鈔")!;
        foreignCash.OpeningBalance.Should().Be(10000m);
        foreignCash.CountsAsAvailableCash.Should().BeFalse();
    }

    [Fact]
    public void Card_and_loan_openings_are_negated()
    {
        var book = LegacyWorkbookMapper.Map(Workbook()).Book;

        book.FindAccount("國泰Combo卡")!.OpeningBalance.Should().Be(-22668m);
        book.FindAccount("房屋貸款")!.OpeningBalance.Should().Be(-2658876m);
        book.FindPlanningFund("財務自由帳戶")!.OpeningBalance.Should().Be(9874.3m);
    }

    [Fact]
    public void Skips_placeholder_names()
    {
        var book = LegacyWorkbookMapper.Map(Workbook()).Book;

        book.FindAccount("貸款備用01").Should().BeNull();
        book.FindCategory("自定浮支09").Should().BeNull();
        book.FindCategory("固定支出", "固定支出10").Should().BeNull();
    }

    [Fact]
    public void Builds_category_tree_with_natures()
    {
        var book = LegacyWorkbookMapper.Map(Workbook()).Book;

        book.FindCategory("主食", "中餐")!.Nature.Should().Be(ExpenseNature.Floating);
        book.FindCategory("固定支出", "行動電話費")!.Nature.Should().Be(ExpenseNature.Fixed);
        book.FindCategory("貸款支出", "房屋貸款")!.Nature.Should().Be(ExpenseNature.Loan);
        book.FindCategory("特別支出")!.Nature.Should().Be(ExpenseNature.Special);
        book.FindCategory("其它收入", "中獎")!.Kind.Should().Be(CategoryKind.Income);
    }

    [Fact]
    public void Opening_date_is_day_before_earliest_corrected_date()
    {
        var january = Month(1, journal:
        [
            Row(8, new DateOnly(2026, 12, 31), "現金", "主食", "晚餐", -180m),
            Row(10, new DateOnly(2026, 1, 2), "現金", "主食", "中餐", -80m),
        ]);

        LegacyWorkbookMapper.Map(Workbook(january)).Book.OpeningDate.Should().Be(new DateOnly(2025, 12, 30));
    }
}
