namespace SixJars.Application.LegacyImport;

/// <summary>舊 Excel 記帳本讀出的原始資料，不含任何語意判斷。</summary>
/// <param name="YearTotals">「總表」的年總計，只供驗收比對；缺表時為 null。</param>
public sealed record LegacyWorkbook(
    LegacySettings Settings,
    IReadOnlyList<LegacyCategoryList> FloatingCategories,
    IReadOnlyList<LegacyMonthSheet> Months,
    LegacyYearFigures? YearTotals = null);

/// <summary>「設定」與「清單」工作表。信用卡與貸款金額為 Excel 原值（尚待繳清、剩餘本金，正數）。</summary>
public sealed record LegacySettings(
    int Year,
    decimal HandCashOpening,
    IReadOnlyList<LegacyNamedAmount> Banks,
    IReadOnlyList<LegacyNamedAmount> CreditCards,
    IReadOnlyList<LegacyNamedAmount> EWallets,
    IReadOnlyList<LegacyNamedAmount> Loans,
    IReadOnlyList<LegacyNamedAmount> PlanningFunds,
    IReadOnlyList<string> IncomeItems,
    IReadOnlyList<string> OtherIncomeSubItems,
    IReadOnlyList<string> FixedExpenseItems);

public sealed record LegacyNamedAmount(string Name, decimal Amount);

public sealed record LegacyCategoryList(string Main, IReadOnlyList<string> Subs);

/// <summary>一張月工作表。LoanPrincipals 為「本月還本金」原值（Excel 為負數）。</summary>
public sealed record LegacyMonthSheet(
    int Month,
    IReadOnlyList<LegacyJournalRow> Journal,
    IReadOnlyList<LegacyTemplateRow> Templates,
    IReadOnlyList<LegacyNamedAmount> LoanPrincipals,
    LegacyMonthFigures Figures);

public sealed record LegacyJournalRow(int Row, DateOnly? Date, string? Method, string? Main, string? Sub, decimal Amount, string? Note);

public enum LegacyTemplateSection { Fixed, Loan, Special }

public sealed record LegacyTemplateRow(int Row, LegacyTemplateSection Section, string Item, string? Method, decimal Amount, DateOnly? PaidDate, string? Note);

/// <summary>Excel 算出的數字，只供驗收比對，不參與匯入。</summary>
/// <param name="FloatingActuals">「預算」工作表 C4:C28 的浮動主分類與該月實際支出欄（Excel 原值，支出為負；spec §9 的 L 段）。</param>
/// <param name="Report">月報表對照用的數字（spec §9 的 M 段）。</param>
public sealed record LegacyMonthFigures(
    decimal MonthlyDisposable,
    decimal WalletAddBack,
    decimal AvailableCash,
    IReadOnlyList<LegacyNamedAmount> BankBalances,
    IReadOnlyList<LegacyNamedAmount> EWalletBalances,
    IReadOnlyList<LegacyNamedAmount> CardOutstanding,
    IReadOnlyList<LegacyNamedAmount> LoanRemaining,
    IReadOnlyList<LegacyNamedAmount> FundBalances,
    IReadOnlyList<LegacyNamedAmount> FloatingActuals,
    LegacyReportFigures? Report = null);

/// <summary>
/// 月工作表上供月報驗收的數字（spec §9 的 M 段），Excel 原值：收入為正、支出為負、出資金為負。
/// 固定、貸款、特別的合計含<b>未付</b>的手填金額（`N` 欄），比對時要加上系統的未付預定小計。
/// </summary>
public sealed record LegacyReportFigures(
    IReadOnlyList<LegacyNamedAmount> IncomeItems,
    IReadOnlyList<LegacyNamedAmount> FloatingMains,
    decimal FloatingTotal,
    decimal FixedTotal,
    decimal LoanTotal,
    decimal SpecialTotal,
    IReadOnlyList<LegacyNamedAmount> FundAllocations,
    IReadOnlyList<LegacyNamedAmount> FundWithdrawals,
    IReadOnlyList<LegacyNamedAmount> FundReturns);

/// <summary>「總表」的年度總計欄（`AU`），只供驗收比對；符號與月工作表相同。</summary>
public sealed record LegacyYearFigures(
    IReadOnlyList<LegacyNamedAmount> IncomeItems,
    IReadOnlyList<LegacyNamedAmount> FloatingMains,
    decimal FloatingTotal,
    decimal FixedTotal,
    decimal LoanTotal,
    decimal SpecialTotal,
    IReadOnlyList<LegacyNamedAmount> FundAllocations);
