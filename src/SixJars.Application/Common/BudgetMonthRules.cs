using FluentValidation;
using SixJars.Domain.Common;

namespace SixJars.Application.Common;

/// <summary>
/// 月份 key（yyyymm）的 validator 規則：規則（<see cref="BudgetMonth.IsValidKey"/>）與錯誤訊息只在這裡定義一次。
/// 不合理的年份若放行，會在 <c>DateOnly</c>／<c>DateTime</c> 擲例外（500），或把無意義的月份存進資料庫。
/// </summary>
internal static class BudgetMonthRules
{
    private const string DefaultLabel = "歸屬月份";

    public static IRuleBuilderOptions<T, int> MustBeBudgetMonthKey<T>(this IRuleBuilder<T, int> rule, string label = DefaultLabel) =>
        rule.Must(BudgetMonth.IsValidKey).WithMessage(Message(label));

    /// <summary>選用欄位：null 表示沒有指定（不篩選、沒有結束），直接通過；有值才檢查。</summary>
    public static IRuleBuilderOptions<T, int?> MustBeBudgetMonthKey<T>(this IRuleBuilder<T, int?> rule, string label = DefaultLabel) =>
        rule.Must(key => key is null || BudgetMonth.IsValidKey(key.Value)).WithMessage(Message(label));

    private static string Message(string label) =>
        $"{label}必須是 yyyymm，年份介於 {BudgetMonth.MinYear} 到 {BudgetMonth.MaxYear}，月份介於 1 到 12。";
}
