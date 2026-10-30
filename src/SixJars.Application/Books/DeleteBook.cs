using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Auditing;
using SixJars.Application.Common;
using SixJars.Domain.Common;

namespace SixJars.Application.Books;

/// <summary>
/// 硬刪除整本帳本（issue #6、ADR 0010）：帳本與所有帶 BookId 的資料都會移除，只留一筆刪除摘要的稽核記錄。
/// <see cref="ConfirmName"/> 必須與帳本名稱完全相同（ordinal、不 trim，spec D2），否則擲 <see cref="DomainException"/>（422 <c>name-mismatch</c>）。
/// 與 DeleteTransaction 的慣例刻意不同：鎖帳日不擋（Q10）、不做樂觀並行（Q7）、任一位擁有者都能刪（Q6）。
/// </summary>
public sealed record DeleteBook(Guid BookId, string ConfirmName) : IRequest, IBookScoped;

internal sealed class DeleteBookValidator : AbstractValidator<DeleteBook>
{
    public DeleteBookValidator() => RuleFor(c => c.ConfirmName).NotEmpty();
}

internal sealed class DeleteBookHandler(ISixJarsDbContext db, IAuditTrail audit, IBookReader books) : IRequestHandler<DeleteBook>
{
    public async Task Handle(DeleteBook request, CancellationToken cancellationToken)
    {
        var book = await books.GetReadOnlyBookAsync(request.BookId, cancellationToken);
        if (!string.Equals(book.Name, request.ConfirmName, StringComparison.Ordinal))
        {
            throw new DomainException($"輸入的名稱與帳本名稱「{book.Name}」不同，沒有刪除。", DomainException.NameMismatchCode);
        }

        // 摘要在刪除前取得（D4），就是使用者在確認對話框裡看到的數字。
        var summary = await BookDeletionPreviews.CreateAsync(db, book, cancellationToken);
        var id = book.Id;

        // ExecuteDelete 不經過 change tracker，也不套用 EF 的 cascade；owned 表（Accounts、Categories、PlanningFunds、
        // Postings、CategoryBudgetOverrides）靠 DB 上的 FK cascade。IgnoreQueryFilters：已軟刪除的交易與預定支出也要刪除（Q14）。
        // 新增帶 BookId 的資料表時，DeleteBookEndpointsTests 的 convention test 會失敗，提醒在這裡補一行（D6）。
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        // PlannedExpenses 以 Restrict FK 指向 RecurringPlannedExpenses，必須先刪（D7）。
        await db.PlannedExpenses.IgnoreQueryFilters().Where(p => p.BookId == id).ExecuteDeleteAsync(cancellationToken);
        await db.RecurringPlannedExpenses.Where(r => r.BookId == id).ExecuteDeleteAsync(cancellationToken);
        await db.CategoryBudgets.Where(b => b.BookId == id).ExecuteDeleteAsync(cancellationToken);
        await db.Transactions.IgnoreQueryFilters().Where(t => t.BookId == id).ExecuteDeleteAsync(cancellationToken);
        await db.BookMembers.Where(m => m.BookId == id).ExecuteDeleteAsync(cancellationToken);
        // 舊的稽核記錄含有完整快照，留著就等於沒刪（Q8）。
        await db.AuditEntries.Where(e => e.BookId == request.BookId).ExecuteDeleteAsync(cancellationToken);
        await db.Books.Where(b => b.Id == id).ExecuteDeleteAsync(cancellationToken);

        audit.Record<BookDeletionPreview>(request.BookId, AuditAction.Delete, AuditEntityTypes.Book, request.BookId, summary, null);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
