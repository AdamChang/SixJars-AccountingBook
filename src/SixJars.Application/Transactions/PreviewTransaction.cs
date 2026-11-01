using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Books;
using SixJars.Application.Common;
using SixJars.Domain.Common;

namespace SixJars.Application.Transactions;

/// <summary>
/// 記帳前的試算（唯讀，不存檔）：回報會建出什麼分錄、是否落在鎖帳日內、來源識別碼是否已記過、有沒有疑似重複。
/// 鎖帳日用 <see cref="TransactionPreview.Locked"/> 回報而不是擲例外，讓 Claude 能清楚告訴使用者。
/// </summary>
public sealed record PreviewTransaction(Guid BookId, TransactionInput Input, string? ExternalRef)
    : IRequest<TransactionPreview>, IBookScoped;

/// <param name="Transaction">會建立的交易；<c>Version</c> 為 0。</param>
/// <param name="Locked">日期是否落在鎖帳日內（含鎖帳日當天）。</param>
/// <param name="ExistingByExternalRef">同一個來源識別碼已存在的交易，沒有則為 null。</param>
/// <param name="SuspectedDuplicates">同帳本、同日、同金額，且動到同一個帳戶（含對方帳戶）的未刪除交易。</param>
public sealed record TransactionPreview(
    TransactionDto Transaction,
    bool Locked,
    TransactionDto? ExistingByExternalRef,
    IReadOnlyList<TransactionDto> SuspectedDuplicates);

internal sealed class PreviewTransactionValidator : AbstractValidator<PreviewTransaction>
{
    public PreviewTransactionValidator() => this.AddBookkeepingInputRules(c => c.Input, c => c.ExternalRef);
}

internal sealed class PreviewTransactionHandler(ISixJarsDbContext db, IBookReader books)
    : IRequestHandler<PreviewTransaction, TransactionPreview>
{
    public async Task<TransactionPreview> Handle(PreviewTransaction request, CancellationToken cancellationToken)
    {
        var bookId = new BookId(request.BookId);
        var input = request.Input;
        var book = await books.GetReadOnlyBookAsync(request.BookId, cancellationToken);

        // 只 Build，不經過 TransactionCreation：鎖帳日要回報而不是擲例外。判斷與 Book.EnsureUnlocked 一致。
        var transaction = TransactionBuilder.Build(book, input);
        var locked = book.LockDate is { } lockDate && input.Date <= lockDate;

        // 這裡的查詢不會被存檔（沒有 SaveChanges）；保持追蹤只是為了讓 db.GetVersion 讀得到版本。
        TransactionDto? existing = null;
        if (request.ExternalRef is { } externalRef)
        {
            var found = await db.Transactions.FirstOrDefaultAsync(t => t.BookId == bookId && t.ExternalRef == externalRef, cancellationToken);
            existing = found is null ? null : TransactionDto.From(found, db.GetVersion(found));
        }

        var accountId = new AccountId(input.AccountId);
        var suspected = await db.Transactions
            .Where(t => t.BookId == bookId && t.Date == input.Date && t.Amount == input.Amount
                && (t.AccountId == accountId || t.CounterAccountId == accountId))
            .OrderBy(t => t.Id)
            .ToListAsync(cancellationToken);

        return new TransactionPreview(
            TransactionDto.From(transaction, 0),
            locked,
            existing,
            [.. suspected.Select(t => TransactionDto.From(t, db.GetVersion(t)))]);
    }
}
