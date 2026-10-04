using SixJars.Domain.Planning;

namespace SixJars.Application.Planning;

/// <summary>預定支出的 API 輸出：輸入欄位加上 Id、付款連結與樂觀並行版本（修改、付款時帶回）。</summary>
/// <param name="BudgetMonth">歸屬月份（yyyymm）。</param>
/// <param name="EstimatedAmount">預估金額，沿用支出的符號慣例（負數）。</param>
public sealed record PlannedExpenseDto(
    Guid Id,
    int BudgetMonth,
    Guid CategoryId,
    Guid? AccountId,
    decimal EstimatedAmount,
    string? Note,
    Guid? PaidTransactionId,
    bool IsPaid,
    uint Version)
{
    public static PlannedExpenseDto From(PlannedExpense p, uint version) => new(
        p.Id.Value,
        p.BudgetMonth.Key,
        p.CategoryId.Value,
        p.AccountId?.Value,
        p.EstimatedAmount,
        p.Note,
        p.PaidTransactionId?.Value,
        p.IsPaid,
        version);
}
