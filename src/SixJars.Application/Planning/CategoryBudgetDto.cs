using SixJars.Domain.Planning;

namespace SixJars.Application.Planning;

public sealed record BudgetOverrideDto(int BudgetMonth, decimal Amount);

/// <summary>
/// 預算的完整快照（P4 L plan Q7）：寫入 API 的回傳、稽核快照與備份 v4 共用。覆寫值依月份排序；不帶版本（Q3）。
/// <see cref="Overrides"/> 是參考型別，比對時要逐項比。
/// </summary>
public sealed record CategoryBudgetDto(Guid CategoryId, decimal? DefaultAmount, IReadOnlyList<BudgetOverrideDto> Overrides)
{
    public static CategoryBudgetDto From(CategoryBudget budget) => new(
        budget.CategoryId.Value,
        budget.DefaultAmount,
        [.. budget.Overrides.OrderBy(o => o.Month.Key).Select(o => new BudgetOverrideDto(o.Month.Key, o.Amount))]);
}
