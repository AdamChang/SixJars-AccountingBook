using SixJars.Domain.Transactions;

namespace SixJars.Application.Transactions;

/// <summary>
/// 交易的平面輸入模型（spec §5）：<see cref="Kind"/> 加上各類型會用到的可選欄位，欄位名稱沿用 <see cref="Transaction"/>。
/// 各類型用到哪些欄位見 <see cref="TransactionInputValidator"/>；轉成 Domain 交易見 <see cref="TransactionBuilder"/>。
/// </summary>
/// <param name="BudgetMonth">歸屬月份（yyyymm）；null 表示取交易日期的月份。</param>
/// <param name="Amount">收入、支出帶正負號（支出為負）；其餘類型為正，貸款繳款為總額。</param>
public sealed record TransactionInput(
    TransactionKind Kind,
    DateOnly Date,
    int? BudgetMonth,
    decimal Amount,
    Guid AccountId,
    Guid? CounterAccountId,
    Guid? CategoryId,
    Guid? PlanningFundId,
    decimal? LoanPrincipal,
    string? Note);
