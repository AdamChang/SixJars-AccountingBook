using MediatR;
using SixJars.Application.Planning;

namespace SixJars.Api.Endpoints;

internal static class PlannedExpensesEndpoints
{
    /// <summary>預定支出的新增、清單與修改；掛在 <c>/books/{bookId}</c> 群組底下。</summary>
    public static RouteGroupBuilder MapPlannedExpensesEndpoints(this RouteGroupBuilder book)
    {
        // 帳本 Id 取自路由。
        book.MapPost("/planned-expenses", async (Guid bookId, PlannedExpenseInput input, ISender sender, CancellationToken ct) =>
        {
            var dto = await sender.Send(new CreatePlannedExpense(bookId, input), ct);
            return Results.Created($"/api/books/{bookId}/planned-expenses/{dto.Id}", dto);
        });
        book.MapGet("/planned-expenses", (Guid bookId, int? budgetMonth, ISender sender, CancellationToken ct) =>
            sender.Send(new ListPlannedExpenses(bookId, budgetMonth), ct));
        book.MapPut("/planned-expenses/{plannedExpenseId:guid}",
            (Guid bookId, Guid plannedExpenseId, UpdatePlannedExpenseBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new UpdatePlannedExpense(bookId, plannedExpenseId, body.Version, body.Input), ct));
        return book;
    }
}

/// <summary>修改預定支出的 body：讀取時拿到的版本，加上新的內容。</summary>
internal sealed record UpdatePlannedExpenseBody(uint Version, PlannedExpenseInput Input);
