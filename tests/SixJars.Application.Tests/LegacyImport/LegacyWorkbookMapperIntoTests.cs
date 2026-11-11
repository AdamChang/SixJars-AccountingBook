using FluentAssertions;
using SixJars.Application.LegacyImport;
using SixJars.Domain.Books;
using Xunit;
using static SixJars.Application.Tests.LegacyImport.LegacyWorkbookFactory;

namespace SixJars.Application.Tests.LegacyImport;

/// <summary>補匯舊年度：把 2025 對應到已由 2026 匯入的帳本（spec D3、D4、R3-Q3）。</summary>
public class LegacyWorkbookMapperIntoTests
{
    private static readonly DateOnly Mar5 = new(2025, 3, 5);

    private readonly Book _book = LegacyWorkbookMapper.Map(Workbook()).Book;

    /// <summary>2025 年的設定：國泰世華銀行期初不同、多一個郵局、少了國泰外幣帳戶。</summary>
    private static LegacySettings Settings2025(IReadOnlyList<LegacyNamedAmount>? banks = null, IReadOnlyList<LegacyNamedAmount>? cards = null) =>
        Settings() with
        {
            Year = 2025,
            Banks = banks ?? [new("國泰世華銀行", 15000m), new("華南銀行", 11637m), new("國泰投資帳戶", 20m), new("郵局", 0m)],
            CreditCards = cards ?? [new("國泰Combo卡", 1000m)],
        };

    private static LegacyWorkbook Workbook2025(LegacySettings settings, IReadOnlyList<LegacyCategoryList>? floating = null) =>
        new(settings, floating ?? Floating(), [Month(3, journal: [Row(7, Mar5, "國泰世華銀行", "主食", "點心", -80m)])]);

    [Fact]
    public void Same_name_settings_map_to_existing_ids_without_touching_openings()
    {
        var bank = _book.FindAccount("國泰世華銀行")!;

        var mapping = LegacyWorkbookMapper.MapInto(Workbook2025(Settings2025()), _book);

        mapping.Report.Errors.Should().BeEmpty();
        mapping.Transactions.Should().ContainSingle().Which.Postings.Should().ContainSingle(p => p.AccountId == bank.Id && p.Amount == -80m);
        bank.OpeningBalance.Should().Be(21610m);
        mapping.AccountOpenings[bank.Id].Should().Be(15000m);
        mapping.AccountOpenings[_book.FindAccount("國泰Combo卡")!.Id].Should().Be(-1000m);
        mapping.FundOpenings[_book.FindPlanningFund("財務自由帳戶")!.Id].Should().Be(9874.3m);
        mapping.OpeningDate.Should().Be(new DateOnly(2025, 3, 4));
    }

    [Fact]
    public void Settings_only_in_previous_year_are_added_unarchived_and_reported()
    {
        var mapping = LegacyWorkbookMapper.MapInto(Workbook2025(Settings2025()), _book);

        var postOffice = _book.FindAccount("郵局")!;
        var snack = _book.FindCategory("主食", "點心")!;
        mapping.AddedAccounts.Should().Equal(postOffice.Id);
        mapping.AddedPlanningFunds.Should().BeEmpty();
        mapping.AddedCategories.Should().Equal(snack.Id);
        postOffice.IsArchived.Should().BeFalse();
        snack.IsArchived.Should().BeFalse();
        mapping.AccountOpenings[postOffice.Id].Should().Be(0m);
    }

    [Fact]
    public void Account_only_in_book_is_not_part_of_openings()
    {
        var mapping = LegacyWorkbookMapper.MapInto(Workbook2025(Settings2025()), _book);

        mapping.AccountOpenings.Should().NotContainKey(_book.FindAccount("國泰外幣帳戶")!.Id);
    }

    [Fact]
    public void Same_name_with_different_account_type_is_an_error()
    {
        var mapping = LegacyWorkbookMapper.MapInto(
            Workbook2025(Settings2025(cards: [new("國泰Combo卡", 1000m), new("華南銀行", 0m)])), _book);

        mapping.Report.Errors.Should().ContainSingle(i => i.Sheet == "設定" && i.Message.Contains("華南銀行"));
        _book.FindAccount("華南銀行")!.Type.Should().Be(AccountType.Bank);
    }

    [Fact]
    public void Different_expense_nature_keeps_the_book_and_warns()
    {
        var transport = _book.AddExpenseCategory("交通", ExpenseNature.Fixed);

        var mapping = LegacyWorkbookMapper.MapInto(
            Workbook2025(Settings2025(), [.. Floating(), new("交通", ["捷運"])]), _book);

        mapping.Report.Errors.Should().BeEmpty();
        mapping.Report.Warnings.Should().ContainSingle(i => i.Sheet == "設定" && i.Message.Contains("交通"));
        transport.Nature.Should().Be(ExpenseNature.Fixed);
        _book.FindCategory("交通", "捷運")!.Nature.Should().Be(ExpenseNature.Fixed);
    }
}
