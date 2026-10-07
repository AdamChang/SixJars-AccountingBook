using SixJars.Domain.Planning;

namespace SixJars.Application.Planning;

/// <summary>週期預定支出的 API 輸出：輸入欄位加上 Id 與樂觀並行版本。<see cref="Months"/> 是參考型別，比對時要逐項比。</summary>
public sealed record RecurringPlannedExpenseDto(
    Guid Id,
    Guid CategoryId,
    Guid? AccountId,
    decimal DefaultAmount,
    string? Note,
    RecurrenceFrequency Frequency,
    IReadOnlyList<int> Months,
    int StartMonth,
    int? EndMonth,
    uint Version)
{
    public static RecurringPlannedExpenseDto From(RecurringPlannedExpense r, uint version) => new(
        r.Id.Value, r.CategoryId.Value, r.AccountId?.Value, r.DefaultAmount, r.Note, r.Frequency,
        [.. r.Months], r.StartMonth.Key, r.EndMonth?.Key, version);
}
