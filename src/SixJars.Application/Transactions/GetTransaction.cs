using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Common;
using SixJars.Domain.Common;

namespace SixJars.Application.Transactions;

public sealed record GetTransaction(Guid BookId, Guid TransactionId) : IRequest<TransactionDto>, IBookScoped;

internal sealed class GetTransactionHandler(ISixJarsDbContext db) : IRequestHandler<GetTransaction, TransactionDto>
{
    public async Task<TransactionDto> Handle(GetTransaction request, CancellationToken cancellationToken)
    {
        // 帳本與交易 Id 一起當查詢條件：只用交易 Id 查詢，就能讀到別本帳的交易。
        var bookId = new BookId(request.BookId);
        var transactionId = new TransactionId(request.TransactionId);
        // 要追蹤變更，db.GetVersion 才讀得到版本。
        var transaction = await db.Transactions
            .SingleOrDefaultAsync(t => t.BookId == bookId && t.Id == transactionId, cancellationToken)
            ?? throw new NotFoundException($"找不到交易 {request.TransactionId}。");

        return TransactionDto.From(transaction, db.GetVersion(transaction));
    }
}
