using FluentAssertions;
using SixJars.Application.LegacyImport;
using SixJars.Domain.Common;
using SixJars.Domain.Transactions;
using Xunit;
using static SixJars.Application.Tests.LegacyImport.LegacyWorkbookFactory;

namespace SixJars.Application.Tests.LegacyImport;

public class LegacyWorkbookMapperJournalTests
{
    private static readonly DateOnly Jan6 = new(2026, 1, 6);

    private static LegacyImportResult MapJanuary(params LegacyJournalRow[] rows) =>
        LegacyWorkbookMapper.Map(Workbook(Month(1, journal: rows)));

    [Fact]
    public void Maps_income_with_sheet_month_as_budget_month()
    {
        var result = MapJanuary(Row(7, new DateOnly(2025, 12, 31), "國泰世華銀行", "工作薪資1", null, 84223m));

        var tx = result.Transactions.Single();
        tx.Kind.Should().Be(TransactionKind.Income);
        tx.Date.Should().Be(new DateOnly(2025, 12, 31));
        tx.BudgetMonth.Should().Be(new BudgetMonth(2026, 1));
        tx.Postings.Should().Equal(new Posting(result.Book.FindAccount("國泰世華銀行")!.Id, 84223m));
    }

    [Fact]
    public void Corrects_mistyped_year_and_reports_it()
    {
        var result = MapJanuary(Row(8, new DateOnly(2026, 12, 31), "現金", "主食", "晚餐", -180m));

        result.Transactions.Single().Date.Should().Be(new DateOnly(2025, 12, 31));
        result.Report.Corrections.Should().ContainSingle(i => i.Row == 8 && i.Sheet == "1月");
    }

    [Fact]
    public void Negative_transfer_goes_from_method_to_sub()
    {
        var result = MapJanuary(Row(35, Jan6, "國泰世華銀行", "轉帳", "華南銀行", -34000m));

        var tx = result.Transactions.Single();
        tx.Kind.Should().Be(TransactionKind.Transfer);
        tx.AccountId.Should().Be(result.Book.FindAccount("國泰世華銀行")!.Id);
        tx.CounterAccountId.Should().Be(result.Book.FindAccount("華南銀行")!.Id);
        tx.Amount.Should().Be(34000m);
    }

    [Fact]
    public void Positive_transfer_goes_from_sub_to_method()
    {
        var result = MapJanuary(Row(31, Jan6, "國泰投資帳戶", "轉帳", "國泰世華銀行", 9000m));

        var tx = result.Transactions.Single();
        tx.AccountId.Should().Be(result.Book.FindAccount("國泰世華銀行")!.Id);
        tx.CounterAccountId.Should().Be(result.Book.FindAccount("國泰投資帳戶")!.Id);
    }

    [Fact]
    public void Maps_withdrawal_card_payment_and_top_up()
    {
        var result = MapJanuary(
            Row(40, Jan6, "國泰世華銀行", "提款", null, -3000m),
            Row(36, Jan6, "國泰世華銀行", "繳信用卡款", "國泰Combo卡", -18197m),
            Row(59, Jan6, "悠遊卡", "加值", "國泰世華銀行", 500m));

        result.Report.Errors.Should().BeEmpty();
        result.Transactions.Select(t => (t.Kind, t.Amount)).Should().Equal(
            (TransactionKind.Withdrawal, 3000m), (TransactionKind.CardPayment, 18197m), (TransactionKind.TopUp, 500m));
    }

    [Fact]
    public void Maps_three_planning_fund_patterns()
    {
        var result = MapJanuary(
            Row(63, Jan6, "國泰投資帳戶", "財務自由帳戶", "國泰世華銀行", 8000m),
            Row(71, Jan6, "國泰投資帳戶", "財務自由帳戶", "出資金", -5492m),
            Row(33, Jan6, "國泰投資帳戶", "財務自由帳戶", "資金回流", 254m));

        result.Report.Errors.Should().BeEmpty();
        result.Transactions.Select(t => (t.Kind, t.FundDelta)).Should().Equal(
            (TransactionKind.FundAllocation, 8000m), (TransactionKind.FundWithdrawal, -5492m), (TransactionKind.FundReturn, 254m));
        result.Transactions[0].CounterAccountId.Should().Be(result.Book.FindAccount("國泰世華銀行")!.Id);
    }

    [Fact]
    public void Non_hand_cash_maps_to_foreign_cash_account()
    {
        var result = MapJanuary(Row(139, Jan6, "非手上現金", "外幣現金", "國泰外幣帳戶", 10000m));

        result.Transactions.Single().AccountId.Should().Be(result.Book.FindAccount("外幣現鈔")!.Id);
    }

    [Fact]
    public void Bank_fee_maps_to_financial_category()
    {
        var result = MapJanuary(Row(20, Jan6, "國泰世華銀行", "手續費", null, -15m));

        result.Transactions.Single().CategoryId.Should().Be(result.Book.FindCategory("金融交易", "手續費")!.Id);
    }

    [Fact]
    public void Unknown_sub_category_is_created_with_warning()
    {
        var result = MapJanuary(Row(18, Jan6, "現金", "主食", "點心", -50m));

        result.Book.FindCategory("主食", "點心").Should().NotBeNull();
        result.Report.Warnings.Should().ContainSingle(i => i.Row == 18);
    }

    [Fact]
    public void Unknown_method_is_an_error_and_row_is_skipped()
    {
        var result = MapJanuary(Row(14, Jan6, "不存在的銀行", "主食", "中餐", -80m));

        result.Transactions.Should().BeEmpty();
        result.Report.Errors.Should().ContainSingle(i => i.Row == 14);
    }

    [Fact]
    public void Cash_into_planning_fund_is_rejected()
    {
        var result = MapJanuary(Row(90, Jan6, "現金", "財務自由帳戶", "入新資金", 1000m));

        result.Transactions.Should().BeEmpty();
        result.Report.Errors.Should().ContainSingle(i => i.Row == 90);
    }
}
