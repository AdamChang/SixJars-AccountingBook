using MediatR;
using SixJars.Application.Ledger;

namespace SixJars.Api.Endpoints;

internal static class SummaryEndpoints
{
    /// <summary>帳務摘要；掛在 <c>/books/{bookId}</c> 群組底下。</summary>
    public static RouteGroupBuilder MapSummaryEndpoints(this RouteGroupBuilder book)
    {
        book.MapGet("/summary", (Guid bookId, int budgetMonth, DateOnly? asOf, ISender sender, CancellationToken ct) =>
            sender.Send(new GetLedgerSummary(bookId, budgetMonth, asOf), ct));
        return book;
    }
}
