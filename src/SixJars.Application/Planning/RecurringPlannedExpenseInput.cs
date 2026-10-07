using FluentValidation;
using SixJars.Domain.Planning;

namespace SixJars.Application.Planning;

/// <summary>週期預定支出的輸入模型；建立與修改共用。</summary>
/// <param name="DefaultAmount">預設金額，沿用支出的符號慣例（負數）。</param>
/// <param name="Months">每年的適用月份；每月時給空陣列。</param>
/// <param name="StartMonth">開始月份（yyyymm）。</param>
/// <param name="EndMonth">結束月份（yyyymm，含）；null 表示沒有結束。</param>
public sealed record RecurringPlannedExpenseInput(
    Guid CategoryId,
    Guid? AccountId,
    decimal DefaultAmount,
    string? Note,
    RecurrenceFrequency Frequency,
    IReadOnlyList<int> Months,
    int StartMonth,
    int? EndMonth);

/// <summary>只檢查形狀；性質、月份清單與起訖由 Domain 檢查（422）。</summary>
public sealed class RecurringPlannedExpenseInputValidator : AbstractValidator<RecurringPlannedExpenseInput>
{
    private const string MonthMessage = "月份必須是 yyyymm，月份介於 1 到 12。";

    public RecurringPlannedExpenseInputValidator()
    {
        RuleFor(i => i.CategoryId).NotEmpty();
        RuleFor(i => i.AccountId).NotEmpty().When(i => i.AccountId is not null);
        // 金額 0 不占用月可用餘額，週期項目就失去意義（Q5）。
        RuleFor(i => i.DefaultAmount).LessThan(0).WithMessage("預設金額必須小於 0（支出以負數表示）。");
        RuleFor(i => i.Note).MaximumLength(500);
        RuleFor(i => i.Frequency).IsInEnum();
        RuleFor(i => i.Months).NotNull();
        RuleFor(i => i.StartMonth).Must(IsMonthKey).WithMessage(MonthMessage);
        RuleFor(i => i.EndMonth).Must(key => IsMonthKey(key!.Value)).When(i => i.EndMonth is not null).WithMessage(MonthMessage);
    }

    private static bool IsMonthKey(int key) => key % 100 is >= 1 and <= 12;
}
