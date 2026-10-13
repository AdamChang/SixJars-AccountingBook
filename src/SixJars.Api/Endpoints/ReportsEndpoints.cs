using MediatR;
using SixJars.Application.Reports;

namespace SixJars.Api.Endpoints;

internal static class ReportsEndpoints
{
    /// <summary>月報與年報（spec §6.2）；掛在 <c>/books/{bookId}</c> 群組底下。</summary>
    public static RouteGroupBuilder MapReportsEndpoints(this RouteGroupBuilder book)
    {
        book.MapGet("/reports/monthly", (Guid bookId, int budgetMonth, ISender sender, CancellationToken ct) =>
            sender.Send(new GetMonthlyReport(bookId, budgetMonth), ct));
        book.MapGet("/reports/yearly", (Guid bookId, int year, ISender sender, CancellationToken ct) =>
            sender.Send(new GetYearlyReport(bookId, year), ct));
        return book;
    }
}
