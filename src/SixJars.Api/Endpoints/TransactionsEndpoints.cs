using MediatR;
using SixJars.Application.Transactions;

namespace SixJars.Api.Endpoints;

internal static class TransactionsEndpoints
{
    /// <summary>交易的新增與讀取；掛在 <c>/books/{bookId}</c> 群組底下。</summary>
    public static RouteGroupBuilder MapTransactionsEndpoints(this RouteGroupBuilder book)
    {
        // body 是平面的 TransactionInput（spec §5），帳本 Id 取自路由。
        book.MapPost("/transactions", async (Guid bookId, TransactionInput input, ISender sender, CancellationToken ct) =>
        {
            var dto = await sender.Send(new CreateTransaction(bookId, input), ct);
            return Results.Created($"/api/books/{bookId}/transactions/{dto.Id}", dto);
        });
        book.MapGet("/transactions/{transactionId:guid}", (Guid bookId, Guid transactionId, ISender sender, CancellationToken ct) =>
            sender.Send(new GetTransaction(bookId, transactionId), ct));
        return book;
    }
}
