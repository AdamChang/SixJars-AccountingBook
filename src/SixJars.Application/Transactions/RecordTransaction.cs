using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Auditing;
using SixJars.Application.Books;
using SixJars.Application.Common;
using SixJars.Domain.Common;
using SixJars.Domain.Transactions;

namespace SixJars.Application.Transactions;

/// <summary>
/// MCP 記帳：以 <paramref name="ExternalRef"/>（來源識別碼，例如信件的 Message-ID）做冪等。
/// 同帳本已有未刪除的同一個識別碼時直接回傳既有交易（<see cref="RecordedTransaction.Duplicate"/>），不寫入、不留稽核記錄。
/// 經由（Via）取自 <see cref="ICurrentUser.Via"/>，不由呼叫端傳入，免得 client 自稱來源。
/// </summary>
public sealed record RecordTransaction(Guid BookId, TransactionInput Input, string? ExternalRef)
    : IRequest<RecordedTransaction>, IBookScoped
{
    /// <summary>AI 記帳只開放從信件容易判斷的種類（spec D7）；財務規劃帳戶與貸款本金讓 Agent 猜容易記錯。</summary>
    public static IReadOnlyList<TransactionKind> SupportedKinds { get; } =
        [TransactionKind.Expense, TransactionKind.Income, TransactionKind.Transfer, TransactionKind.CardPayment, TransactionKind.Withdrawal];

    /// <summary>來源識別碼長度上限，與資料庫欄位一致。</summary>
    public const int ExternalRefMaxLength = 255;
}

/// <param name="Duplicate">true 表示是既有交易（同一個來源識別碼），這次沒有寫入。</param>
public sealed record RecordedTransaction(TransactionDto Transaction, bool Duplicate);

internal sealed class RecordTransactionValidator : AbstractValidator<RecordTransaction>
{
    public RecordTransactionValidator() => this.AddBookkeepingInputRules(c => c.Input, c => c.ExternalRef);
}

internal sealed class RecordTransactionHandler(ISixJarsDbContext db, IAuditTrail audit, IBookReader books, ICurrentUser user)
    : IRequestHandler<RecordTransaction, RecordedTransaction>
{
    public async Task<RecordedTransaction> Handle(RecordTransaction request, CancellationToken cancellationToken)
    {
        var bookId = new BookId(request.BookId);

        // 查詢必須帶 BookId；Transaction 的 query filter 已排除軟刪除，所以刪掉記錯的那筆後可以用同一個識別碼重記。
        // 重複時先於鎖帳日檢查就回傳：那是既有資料，不是新寫入。
        if (request.ExternalRef is { } externalRef
            && await db.Transactions.FirstOrDefaultAsync(t => t.BookId == bookId && t.ExternalRef == externalRef, cancellationToken)
                is { } existing)
        {
            return new RecordedTransaction(TransactionDto.From(existing, db.GetVersion(existing)), Duplicate: true);
        }

        var book = await books.GetReadOnlyBookAsync(request.BookId, cancellationToken);
        var transaction = TransactionCreation.Create(book, request.Input);
        transaction.SetOrigin(user.Via, request.ExternalRef);
        db.Transactions.Add(transaction);

        // 快照要在 SetOrigin 之後取，才會包含來源。
        audit.Record(request.BookId, AuditAction.Create, AuditEntityTypes.Transaction, transaction.Id.Value,
            null, TransactionDto.From(transaction, AuditSnapshots.UnknownVersion));
        await db.SaveChangesAsync(cancellationToken);

        return new RecordedTransaction(TransactionDto.From(transaction, db.GetVersion(transaction)), Duplicate: false);
    }
}
