using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Auditing;
using SixJars.Application.Books;
using SixJars.Application.Common;
using SixJars.Application.Transactions;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Transactions;

namespace SixJars.Application.Planning;

/// <summary>
/// 預定支出付款：依分類的支出性質建立 Expense 或 LoanPayment 交易，並連結到預定支出（規則同 P1 制式表格匯入，見 MappingSession.Templates）。
/// 交易的歸屬月份是預定支出的歸屬月份，不是付款日期所在的月份。
/// </summary>
/// <param name="Version">讀取預定支出時拿到的版本；期間有人改過就擲 <see cref="DbUpdateConcurrencyException"/>（409）。</param>
/// <param name="Amount">實際金額，沿用預定支出的符號慣例（負數）。</param>
/// <param name="LoanAccountId">貸款性質必填：被償還的貸款帳戶；其他性質必須留空。</param>
/// <param name="LoanPrincipal">貸款性質必填：本次償還的本金；其他性質必須留空。</param>
public sealed record PayPlannedExpense(
    Guid BookId,
    Guid PlannedExpenseId,
    uint Version,
    DateOnly Date,
    Guid AccountId,
    decimal Amount,
    Guid? LoanAccountId,
    decimal? LoanPrincipal) : IRequest<PayPlannedExpenseResult>, IBookScoped;

/// <summary>付款結果：兩者都帶最新的版本，前端可以接著修改。</summary>
public sealed record PayPlannedExpenseResult(PlannedExpenseDto PlannedExpense, TransactionDto Transaction);

/// <summary>只檢查形狀；貸款欄位是否必填取決於分類的支出性質，要讀帳本才知道，交給 handler（422）。</summary>
internal sealed class PayPlannedExpenseValidator : AbstractValidator<PayPlannedExpense>
{
    public PayPlannedExpenseValidator()
    {
        RuleFor(c => c.AccountId).NotEmpty();
        RuleFor(c => c.LoanAccountId).NotEmpty().When(c => c.LoanAccountId is not null);
    }
}

internal sealed class PayPlannedExpenseHandler(ISixJarsDbContext db, IAuditTrail audit) : IRequestHandler<PayPlannedExpense, PayPlannedExpenseResult>
{
    public async Task<PayPlannedExpenseResult> Handle(PayPlannedExpense request, CancellationToken cancellationToken)
    {
        var planned = await db.FindPlannedExpenseAsync(request.BookId, request.PlannedExpenseId, cancellationToken);

        // 帳本只用來驗證帳戶、分類與鎖帳日，不會被修改。
        var book = await db.GetBookAsNoTrackingAsync(request.BookId, cancellationToken);
        // 付款同時新增交易（看付款日期）與修改預定支出（看歸屬月份），兩者都要開放。
        book.EnsureUnlocked(request.Date);
        book.EnsureUnlocked(planned.BudgetMonth);
        var transaction = BuildPayment(book, planned.CategoryId, planned.BudgetMonth, planned.Note, request);

        // 修改前的快照必須在 MarkPaid 之前取得。
        var before = PlannedExpenseDto.From(planned, db.GetVersion(planned));
        db.ExpectVersion(planned, request.Version);
        planned.MarkPaid(transaction);
        // 新增交易、付款連結與兩筆稽核記錄（交易的 Create、預定支出的 Update）在同一次 SaveChanges，一起成功或一起失敗。
        db.Transactions.Add(transaction);
        audit.Record(request.BookId, AuditAction.Create, AuditEntityTypes.Transaction, transaction.Id.Value,
            null, TransactionDto.From(transaction, AuditSnapshots.UnknownVersion));
        audit.Record(request.BookId, AuditAction.Update, AuditEntityTypes.PlannedExpense, planned.Id.Value,
            before, PlannedExpenseDto.From(planned, AuditSnapshots.UnknownVersion));
        await db.SaveChangesAsync(cancellationToken);

        return new PayPlannedExpenseResult(
            PlannedExpenseDto.From(planned, db.GetVersion(planned)),
            TransactionDto.From(transaction, db.GetVersion(transaction)));
    }

    private static Transaction BuildPayment(Book book, CategoryId categoryId, BudgetMonth budgetMonth, string? note, PayPlannedExpense request)
    {
        var factory = new TransactionFactory(book);
        var payer = new AccountId(request.AccountId);

        if (book.GetCategory(categoryId).Nature == ExpenseNature.Loan)
        {
            var loan = request.LoanAccountId ?? throw new DomainException("貸款支出付款必須填寫 LoanAccountId。");
            var principal = request.LoanPrincipal ?? throw new DomainException("貸款支出付款必須填寫 LoanPrincipal。");
            // 利息記在預定支出的分類。
            return factory.LoanPayment(request.Date, payer, new AccountId(loan), -request.Amount, principal, categoryId, note, budgetMonth);
        }

        if (request.LoanAccountId is not null || request.LoanPrincipal is not null)
        {
            throw new DomainException("非貸款支出付款不使用 LoanAccountId 與 LoanPrincipal，必須留空。");
        }

        return factory.Expense(request.Date, payer, categoryId, request.Amount, note, budgetMonth);
    }
}
