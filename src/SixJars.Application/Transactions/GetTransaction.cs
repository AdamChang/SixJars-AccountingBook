using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Common;
using SixJars.Domain.Common;

namespace SixJars.Application.Transactions;

public sealed record GetTransaction(Guid BookId, Guid TransactionId) : IRequest<TransactionDto>;

internal sealed class GetTransactionHandler(ISixJarsDbContext db) : IRequestHandler<GetTransaction, TransactionDto>
{
    public async Task<TransactionDto> Handle(GetTransaction request, CancellationToken cancellationToken)
    {
        // 帳本與交易 Id 一起當查詢條件：只用交易 Id 查詢，就能讀到別本帳的交易。
        var bookId = new BookId(request.BookId);
        var transactionId = new TransactionId(request.TransactionId);
        var transaction = await db.Transactions.AsNoTracking()
            .SingleOrDefaultAsync(t => t.BookId == bookId && t.Id == transactionId, cancellationToken)
            ?? throw new NotFoundException($"找不到交易 {request.TransactionId}。");

        // xmin 還沒建模（db.GetVersion 會擲例外），版本先回傳 0；T23 改為追蹤查詢並接上 db.GetVersion。
        return TransactionDto.From(transaction, version: 0);
    }
}
