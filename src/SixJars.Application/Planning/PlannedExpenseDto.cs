using SixJars.Domain.Planning;

namespace SixJars.Application.Planning;

/// <summary>預定支出的 API 輸出：輸入欄位加上 Id、付款連結、來源週期項目與樂觀並行版本（修改、付款時帶回）。</summary>
/// <param name="BudgetMonth">歸屬月份（yyyymm）。</param>
/// <param name="EstimatedAmount">預估金額，沿用支出的符號慣例（負數）。</param>
/// <param name="SourceId">產生它的週期項目；手動新增的為 null。選用參數：v1、v2 的備份沒有這個欄位（P4 K plan D3）。</param>
public sealed record PlannedExpenseDto(
    Guid Id,
    int BudgetMonth,
    Guid CategoryId,
    Guid? AccountId,
    decimal EstimatedAmount,
    string? Note,
    Guid? PaidTransactionId,
    bool IsPaid,
    uint Version,
    Guid? SourceId = null)
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
        version,
        p.SourceId?.Value);
}
