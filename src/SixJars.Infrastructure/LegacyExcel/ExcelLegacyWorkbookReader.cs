using System.Text;
using SixJars.Application.LegacyImport;

namespace SixJars.Infrastructure.LegacyExcel;

/// <summary>
/// 讀取舊記帳本 xlsm：只做「儲存格 → DTO」，不做任何語意判斷（語意在 Application 的匯入轉換）。
/// 儲存格位址依 2026 年版記帳本查證。
/// </summary>
public sealed class ExcelLegacyWorkbookReader : ILegacyWorkbookReader
{
    private const string SettingsSheet = "設定";
    private const string ListSheet = "清單";
    private const string BudgetSheet = "預算";

    static ExcelLegacyWorkbookReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public async Task<LegacyWorkbook> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        var sheets = SheetGrid.LoadAll(buffer);
        // 「預算」工作表只供驗收比對（P4 L plan L9），缺表時不影響匯入
        var budget = sheets.GetValueOrDefault(BudgetSheet);
        var months = Enumerable.Range(1, 12)
            .Where(month => sheets.ContainsKey($"{month}月"))
            .Select(month => ReadMonth(month, sheets[$"{month}月"], budget))
            .ToList();

        return new LegacyWorkbook(ReadSettings(sheets[SettingsSheet], sheets[ListSheet]), ReadFloatingCategories(sheets[ListSheet]), months);
    }

    private static LegacySettings ReadSettings(SheetGrid settings, SheetGrid list) => new(
        Year: (int)settings.Number("J4"),
        HandCashOpening: settings.Number("E54"),
        Banks: settings.NamedAmounts("B", "D", 70, 79),
        CreditCards: settings.NamedAmounts("G", "I", 70, 79),
        EWallets: settings.NamedAmounts("G", "I", 59, 66),
        Loans: settings.NamedAmounts("B", "D", 83, 90),
        PlanningFunds: settings.NamedAmounts("B", "D", 59, 66),
        IncomeItems: settings.Texts("B", 11, 18),
        OtherIncomeSubItems: list.Texts("AU", 4, 120),
        FixedExpenseItems: settings.Texts("B", 22, 41));

    private static IReadOnlyList<LegacyCategoryList> ReadFloatingCategories(SheetGrid list) =>
    [
        .. SheetGrid.Columns("AV", "BT")
            .Where(column => list.Text($"{column}2") is not null)
            .Select(column => new LegacyCategoryList(list.Text($"{column}2")!, list.Texts(column, 4, 120))),
    ];

    private static LegacyMonthSheet ReadMonth(int month, SheetGrid sheet, SheetGrid? budget) => new(
        month,
        ReadJournal(sheet),
        ReadTemplates(sheet),
        sheet.NamedAmounts("E", "N", 124, 131),
        ReadFigures(sheet, budget, month));

    private static IReadOnlyList<LegacyJournalRow> ReadJournal(SheetGrid sheet) =>
    [
        .. Enumerable.Range(7, 172 - 7 + 1)
            .Where(row => sheet.HasNumber($"AL{row}"))
            .Select(row => new LegacyJournalRow(
                row,
                sheet.Date($"AH{row}"),
                sheet.Text($"AI{row}"),
                sheet.Text($"AJ{row}"),
                sheet.Text($"AK{row}"),
                sheet.Number($"AL{row}"),
                sheet.Text($"AM{row}"))),
    ];

    private static IReadOnlyList<LegacyTemplateRow> ReadTemplates(SheetGrid sheet) =>
    [
        .. TemplateSection(sheet, LegacyTemplateSection.Fixed, 50, 69, noteColumn: null),
        .. TemplateSection(sheet, LegacyTemplateSection.Loan, 74, 81, noteColumn: "AB"),
        .. TemplateSection(sheet, LegacyTemplateSection.Special, 86, 91, noteColumn: "X"),
    ];

    private static IEnumerable<LegacyTemplateRow> TemplateSection(SheetGrid sheet, LegacyTemplateSection section, int first, int last, string? noteColumn) =>
        Enumerable.Range(first, last - first + 1)
            .Where(row => sheet.Text($"E{row}") is not null && sheet.Number($"N{row}") != 0m)
            .Select(row => new LegacyTemplateRow(
                row,
                section,
                sheet.Text($"E{row}")!,
                sheet.Text($"J{row}"),
                sheet.Number($"N{row}"),
                sheet.Date($"T{row}"),
                noteColumn is null ? null : sheet.Text($"{noteColumn}{row}")));

    private static LegacyMonthFigures ReadFigures(SheetGrid sheet, SheetGrid? budget, int month) => new(
        MonthlyDisposable: sheet.Number("J6"),
        WalletAddBack: sheet.Number("N5"),
        AvailableCash: sheet.Number("E6"),
        BankBalances: sheet.NamedAmounts("E", "X", 96, 105),
        EWalletBalances: sheet.NamedAmounts("E", "M", 22, 29),
        CardOutstanding: sheet.NamedAmounts("E", "X", 110, 119),
        LoanRemaining: sheet.NamedAmounts("E", "X", 124, 131),
        FundBalances: sheet.NamedAmounts("S", "AA", 10, 17),
        FloatingActuals: budget?.NamedAmounts("C", BudgetActualColumn(month), 4, 28) ?? []);

    /// <summary>「預算」工作表每月三欄（預算分配、實際支出、誤差），1 月的實際支出在 E 欄，12 月在 AL 欄。</summary>
    private static string BudgetActualColumn(int month) =>
        SheetGrid.Columns("E", "AL").Where((_, index) => index % 3 == 0).ElementAt(month - 1);
}
