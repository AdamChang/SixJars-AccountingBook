namespace SixJars.Application.Ledger;

/// <summary>
/// 帳務摘要（spec §5 <c>/summary</c>）。月可用餘額與年累計依歸屬月份；可用現金與各項餘額依日期截止（資產負債表）。
/// </summary>
/// <param name="BudgetMonth">歸屬月份（yyyymm）。</param>
/// <param name="AsOf">餘額的截止日（含）。</param>
/// <param name="YearToDate">同一年 1 月到 <paramref name="BudgetMonth"/> 的月可用餘額加總。</param>
/// <param name="AvailableCash">計入可用現金的現金帳戶餘額加總。</param>
/// <param name="AvailableCashWithEWallets">可用現金再加上電子錢包餘額。</param>
public sealed record LedgerSummaryDto(
    int BudgetMonth,
    DateOnly AsOf,
    decimal MonthlyDisposable,
    decimal YearToDate,
    decimal AvailableCash,
    decimal AvailableCashWithEWallets,
    IReadOnlyList<BalanceDto> Accounts,
    IReadOnlyList<BalanceDto> PlanningFunds);

/// <summary>帳戶或財務規劃帳戶在截止日的餘額（期初加上變動）。</summary>
public sealed record BalanceDto(Guid Id, string Name, decimal Balance);
