using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Auditing;
using SixJars.Application.Books;
using SixJars.Application.Common;
using SixJars.Domain.Common;

namespace SixJars.Application.Transactions;

/// <summary>
/// 修改一筆交易（可改交易類型），以樂觀並行控制：<see cref="Version"/> 是前端讀到的版本，
/// 期間若有人改過這筆交易，存檔時擲 <see cref="DbUpdateConcurrencyException"/>（409）。
/// </summary>
public sealed record UpdateTransaction(Guid BookId, Guid TransactionId, uint Version, TransactionInput Input)
    : IRequest<TransactionDto>, IBookScoped;

internal sealed class UpdateTransactionValidator : AbstractValidator<UpdateTransaction>
{
    public UpdateTransactionValidator() => RuleFor(c => c.Input).NotNull().SetValidator(new TransactionInputValidator());
}

internal sealed class UpdateTransactionHandler(ISixJarsDbContext db, IAuditTrail audit) : IRequestHandler<UpdateTransaction, TransactionDto>
{
    public async Task<TransactionDto> Handle(UpdateTransaction request, CancellationToken cancellationToken)
    {
        // 帳本與交易 Id 一起當查詢條件：只用交易 Id 查詢，就能改到別本帳的交易。
        var bookId = new BookId(request.BookId);
        var transactionId = new TransactionId(request.TransactionId);
        var transaction = await db.Transactions
            .SingleOrDefaultAsync(t => t.BookId == bookId && t.Id == transactionId, cancellationToken)
            ?? throw new NotFoundException($"找不到交易 {request.TransactionId}。");

        // 帳本只用來驗證帳戶、分類與鎖帳日，不會被修改。
        var book = await db.GetBookAsNoTrackingAsync(request.BookId, cancellationToken);
        // 原日期與新日期都要檢查：不能把交易搬進或搬出鎖定區間（spec §3.1）。
        book.EnsureUnlocked(transaction.Date);
        book.EnsureUnlocked(request.Input.Date);
        var draft = TransactionBuilder.Build(book, request.Input);

        // 修改前的快照必須在 ReplaceWith 之前取得，否則會拿到修改後的內容。
        var before = TransactionDto.From(transaction, db.GetVersion(transaction));
        db.ExpectVersion(transaction, request.Version);
        transaction.ReplaceWith(draft);
        audit.Record(request.BookId, AuditAction.Update, AuditEntityTypes.Transaction, transaction.Id.Value,
            before, TransactionDto.From(transaction, AuditSnapshots.UnknownVersion));
        await db.SaveChangesAsync(cancellationToken);

        return TransactionDto.From(transaction, db.GetVersion(transaction));
    }
}
