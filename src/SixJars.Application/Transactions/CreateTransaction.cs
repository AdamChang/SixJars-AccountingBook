using FluentValidation;
using MediatR;
using SixJars.Application.Auditing;
using SixJars.Application.Books;
using SixJars.Application.Common;

namespace SixJars.Application.Transactions;

/// <summary>新增一筆交易。形狀由 <see cref="TransactionInputValidator"/> 檢查（400），業務規則由 TransactionFactory 檢查（422）。</summary>
public sealed record CreateTransaction(Guid BookId, TransactionInput Input) : IRequest<TransactionDto>, IBookScoped;

internal sealed class CreateTransactionValidator : AbstractValidator<CreateTransaction>
{
    public CreateTransactionValidator() => RuleFor(c => c.Input).NotNull().SetValidator(new TransactionInputValidator());
}

internal sealed class CreateTransactionHandler(ISixJarsDbContext db, IAuditTrail audit) : IRequestHandler<CreateTransaction, TransactionDto>
{
    public async Task<TransactionDto> Handle(CreateTransaction request, CancellationToken cancellationToken)
    {
        // 帳本只用來驗證帳戶、分類與鎖帳日，不會被修改。
        var book = await db.GetBookAsNoTrackingAsync(request.BookId, cancellationToken);
        book.EnsureUnlocked(request.Input.Date);
        var transaction = TransactionBuilder.Build(book, request.Input);
        db.Transactions.Add(transaction);
        audit.Record(request.BookId, AuditAction.Create, AuditEntityTypes.Transaction, transaction.Id.Value,
            null, TransactionDto.From(transaction, AuditSnapshots.UnknownVersion));
        await db.SaveChangesAsync(cancellationToken);

        // 存檔時 EF 已讀回資料庫產生的版本。
        return TransactionDto.From(transaction, db.GetVersion(transaction));
    }
}
