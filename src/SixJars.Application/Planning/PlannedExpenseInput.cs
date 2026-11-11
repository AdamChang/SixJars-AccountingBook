using FluentValidation;
using SixJars.Application.Common;

namespace SixJars.Application.Planning;

/// <summary>預定支出的輸入模型；建立與修改共用。</summary>
/// <param name="BudgetMonth">歸屬月份（yyyymm）。</param>
/// <param name="AccountId">預計的付款帳戶；可不指定。</param>
/// <param name="EstimatedAmount">預估金額，沿用支出的符號慣例（負數）。</param>
public sealed record PlannedExpenseInput(int BudgetMonth, Guid CategoryId, Guid? AccountId, decimal EstimatedAmount, string? Note);

/// <summary>只檢查形狀；分類的支出性質、帳戶是否屬於帳本等業務規則交給 Domain（422）。</summary>
public sealed class PlannedExpenseInputValidator : AbstractValidator<PlannedExpenseInput>
{
    public PlannedExpenseInputValidator()
    {
        RuleFor(i => i.BudgetMonth).MustBeBudgetMonthKey();
        RuleFor(i => i.CategoryId).NotEmpty();
        RuleFor(i => i.AccountId).NotEmpty().When(i => i.AccountId is not null);
        RuleFor(i => i.Note).MaximumLength(500);
    }
}
