using FluentValidation;
using SixJars.Domain.Transactions;
using static SixJars.Domain.Transactions.TransactionKind;

namespace SixJars.Application.Transactions;

/// <summary>
/// 只檢查 <see cref="TransactionInput"/> 的形狀：依交易類型檢查必填欄位，沒有作用的欄位必須為 null
/// （避免前端送了沒有作用的欄位，卻以為已經存檔）。正負號與帳戶類型屬於業務規則，交給 <see cref="TransactionFactory"/>。
/// </summary>
/// <remarks>
/// 各類型的欄位（必填／選填／不使用）：
/// <list type="table">
/// <listheader><term>類型</term><description>CounterAccountId、CategoryId、PlanningFundId、LoanPrincipal</description></listheader>
/// <item><term>Income、Expense</term><description>不使用、必填、不使用、不使用</description></item>
/// <item><term>Transfer、Withdrawal、CashDeposit、TopUp、CardPayment、LoanDisbursement</term><description>必填、不使用、不使用、不使用</description></item>
/// <item><term>LoanPayment</term><description>必填、選填（利息分類）、不使用、必填</description></item>
/// <item><term>FundAllocation</term><description>選填（轉出帳戶）、不使用、必填、不使用</description></item>
/// <item><term>FundWithdrawal</term><description>不使用、選填、必填、不使用</description></item>
/// <item><term>FundReturn</term><description>不使用、不使用、必填、不使用</description></item>
/// </list>
/// </remarks>
public sealed class TransactionInputValidator : AbstractValidator<TransactionInput>
{
    private static readonly TransactionKind[] CounterAccountRequired =
        [Transfer, Withdrawal, CashDeposit, TopUp, CardPayment, LoanDisbursement, LoanPayment];
    private static readonly TransactionKind[] CounterAccountAllowed = [.. CounterAccountRequired, FundAllocation];

    private static readonly TransactionKind[] CategoryRequired = [Income, Expense];
    private static readonly TransactionKind[] CategoryAllowed = [.. CategoryRequired, LoanPayment, FundWithdrawal];

    private static readonly TransactionKind[] PlanningFundRequired = [FundAllocation, FundWithdrawal, FundReturn];

    private static readonly TransactionKind[] LoanPrincipalRequired = [LoanPayment];

    public TransactionInputValidator()
    {
        RuleFor(i => i.Kind).IsInEnum();
        RuleFor(i => i.AccountId).NotEmpty();

        RuleFor(i => i.CounterAccountId).NotEmpty().When(i => CounterAccountRequired.Contains(i.Kind));
        RuleFor(i => i.CounterAccountId).Null().When(i => !CounterAccountAllowed.Contains(i.Kind)).WithMessage(Unused);

        RuleFor(i => i.CategoryId).NotEmpty().When(i => CategoryRequired.Contains(i.Kind));
        RuleFor(i => i.CategoryId).Null().When(i => !CategoryAllowed.Contains(i.Kind)).WithMessage(Unused);

        RuleFor(i => i.PlanningFundId).NotEmpty().When(i => PlanningFundRequired.Contains(i.Kind));
        RuleFor(i => i.PlanningFundId).Null().When(i => !PlanningFundRequired.Contains(i.Kind)).WithMessage(Unused);

        RuleFor(i => i.LoanPrincipal).NotNull().When(i => LoanPrincipalRequired.Contains(i.Kind));
        RuleFor(i => i.LoanPrincipal).Null().When(i => !LoanPrincipalRequired.Contains(i.Kind)).WithMessage(Unused);

        RuleFor(i => i.Note).MaximumLength(500);
        RuleFor(i => i.BudgetMonth)
            .Must(key => key!.Value % 100 is >= 1 and <= 12)
            .When(i => i.BudgetMonth is not null)
            .WithMessage("歸屬月份必須是 yyyymm，月份介於 1 到 12。");
    }

    private static string Unused(TransactionInput input) => $"{input.Kind} 交易不使用此欄位，必須留空。";
}
