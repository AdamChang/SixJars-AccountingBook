using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Transactions;

namespace SixJars.Application.Transactions;

/// <summary>把平面的 <see cref="TransactionInput"/> 轉成 Domain 交易：依類型呼叫對應的 <see cref="TransactionFactory"/> 方法。</summary>
public static class TransactionBuilder
{
    /// <summary>
    /// 必填欄位在 API 已由 <see cref="TransactionInputValidator"/> 保證；CLI 還原（T42）不經過 validator，
    /// 所以缺欄位時仍擲 <see cref="DomainException"/>。業務規則（帳戶類型、正負號等）由 factory 檢查。
    /// </summary>
    /// <param name="fixedId">只有還原備份時指定，保留原本的 Id（見 <see cref="TransactionFactory"/>）。</param>
    public static Transaction Build(Book book, TransactionInput input, TransactionId? fixedId = null)
    {
        var factory = new TransactionFactory(book, fixedId);
        var date = input.Date;
        var amount = input.Amount;
        var account = new AccountId(input.AccountId);
        var budgetMonth = input.BudgetMonth is { } key ? BudgetMonth.FromKey(key) : (BudgetMonth?)null;
        var note = input.Note;

        var transaction = input.Kind switch
        {
            TransactionKind.Income => factory.Income(date, account, Category(), amount, note, budgetMonth),
            TransactionKind.Expense => factory.Expense(date, account, Category(), amount, note, budgetMonth),
            TransactionKind.Transfer => factory.Transfer(date, account, Counter(), amount, note, budgetMonth),
            TransactionKind.Withdrawal => factory.Withdrawal(date, account, Counter(), amount, note, budgetMonth),
            TransactionKind.CashDeposit => factory.CashDeposit(date, account, Counter(), amount, note, budgetMonth),
            TransactionKind.TopUp => factory.TopUp(date, account, Counter(), amount, note, budgetMonth),
            TransactionKind.CardPayment => factory.CardPayment(date, account, Counter(), amount, note, budgetMonth),
            TransactionKind.LoanDisbursement => factory.LoanDisbursement(date, account, Counter(), amount, note, budgetMonth),
            TransactionKind.LoanPayment => factory.LoanPayment(
                date, account, Counter(), amount, Required(input.LoanPrincipal, nameof(input.LoanPrincipal)),
                OptionalCategory(), note, budgetMonth),
            TransactionKind.FundAllocation => factory.FundAllocation(
                date, Fund(), account, input.CounterAccountId is { } from ? new AccountId(from) : null, amount, note, budgetMonth),
            TransactionKind.FundWithdrawal => factory.FundWithdrawal(date, Fund(), account, amount, OptionalCategory(), note, budgetMonth),
            TransactionKind.FundReturn => factory.FundReturn(date, Fund(), account, amount, note, budgetMonth),
            _ => throw new DomainException($"不支援的交易類型 {input.Kind}。"),
        };
        return factory.WithTags(transaction, [.. (input.TagIds ?? []).Select(id => new TagId(id))]);

        AccountId Counter() => new(Required(input.CounterAccountId, nameof(input.CounterAccountId)));
        CategoryId Category() => new(Required(input.CategoryId, nameof(input.CategoryId)));
        CategoryId? OptionalCategory() => input.CategoryId is { } id ? new CategoryId(id) : null;
        PlanningFundId Fund() => new(Required(input.PlanningFundId, nameof(input.PlanningFundId)));

        T Required<T>(T? value, string field) where T : struct =>
            value ?? throw new DomainException($"{input.Kind} 交易必須填寫 {field}。");
    }

    /// <summary>
    /// 封存的標籤不可新加；修改交易時原本就掛著的可以保留（tags spec G7、D2）。
    /// 不放在 factory：還原備份要能重建掛著封存標籤的交易。
    /// </summary>
    /// <param name="existing">新增交易時為空集合；修改時是 ReplaceWith 之前的標籤。</param>
    internal static void EnsureNoNewArchivedTags(Book book, IReadOnlyCollection<TagId> existing, IReadOnlyCollection<TagId> requested)
    {
        var archived = requested.Except(existing).Select(book.GetTag).FirstOrDefault(t => t.IsArchived);
        if (archived is not null)
        {
            throw new DomainException($"標籤「{archived.Name}」已封存，不能再加到交易上。");
        }
    }
}
