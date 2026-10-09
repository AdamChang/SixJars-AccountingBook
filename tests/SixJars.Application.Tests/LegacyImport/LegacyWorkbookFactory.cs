using SixJars.Application.LegacyImport;

namespace SixJars.Application.Tests.LegacyImport;

/// <summary>合成的舊記帳本；結構與真實檔案相同，但只保留測試需要的項目。</summary>
internal static class LegacyWorkbookFactory
{
    private static readonly LegacyMonthFigures NoFigures = new(0m, 0m, 0m, [], [], [], [], [], []);

    public static LegacySettings Settings(
        IReadOnlyList<LegacyNamedAmount>? loans = null,
        IReadOnlyList<LegacyNamedAmount>? funds = null) => new(
        Year: 2026,
        HandCashOpening: 2071m,
        Banks: [new("國泰世華銀行", 21610m), new("華南銀行", 11637m), new("國泰投資帳戶", 20m), new("國泰外幣帳戶", 16365m)],
        CreditCards: [new("國泰Combo卡", 22668m)],
        EWallets: [new("悠遊卡", 152m)],
        Loans: loans ?? [new("房屋貸款", 2658876m), new("貸款備用01", 0m)],
        PlanningFunds: funds ?? [new("財務自由帳戶", 9874.3m), new("外幣現金", 10000m)],
        IncomeItems: ["工作薪資1", "其它收入"],
        OtherIncomeSubItems: ["中獎"],
        FixedExpenseItems: ["行動電話費", "保險費", "固定支出10"]);

    public static IReadOnlyList<LegacyCategoryList> Floating() =>
        [new("主食", ["早餐", "中餐", "晚餐", "宵夜"]), new("金融交易", ["手續費"]), new("自定浮支09", [])];

    public static LegacyMonthSheet Month(
        int month,
        IReadOnlyList<LegacyJournalRow>? journal = null,
        IReadOnlyList<LegacyTemplateRow>? templates = null,
        IReadOnlyList<LegacyNamedAmount>? loanPrincipals = null) =>
        new(month, journal ?? [], templates ?? [], loanPrincipals ?? [], NoFigures);

    public static LegacyWorkbook Workbook(params LegacyMonthSheet[] months) => new(Settings(), Floating(), months);

    public static LegacyWorkbook Workbook(LegacySettings settings, params LegacyMonthSheet[] months) => new(settings, Floating(), months);

    public static LegacyJournalRow Row(int row, DateOnly date, string method, string main, string? sub, decimal amount) =>
        new(row, date, method, main, sub, amount, null);
}
