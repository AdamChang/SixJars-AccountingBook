namespace SixJars.Application.LegacyImport;

/// <summary>舊 Excel 記帳本讀出的原始資料，不含任何語意判斷。</summary>
public sealed record LegacyWorkbook(LegacySettings Settings, IReadOnlyList<LegacyCategoryList> FloatingCategories, IReadOnlyList<LegacyMonthSheet> Months);

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
public sealed record LegacyMonthFigures(
    decimal MonthlyDisposable,
    decimal WalletAddBack,
    decimal AvailableCash,
    IReadOnlyList<LegacyNamedAmount> BankBalances,
    IReadOnlyList<LegacyNamedAmount> EWalletBalances,
    IReadOnlyList<LegacyNamedAmount> CardOutstanding,
    IReadOnlyList<LegacyNamedAmount> LoanRemaining,
    IReadOnlyList<LegacyNamedAmount> FundBalances);
