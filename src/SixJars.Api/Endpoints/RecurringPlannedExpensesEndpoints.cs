using MediatR;
using SixJars.Application.Planning;

namespace SixJars.Api.Endpoints;

internal static class RecurringPlannedExpensesEndpoints
{
    /// <summary>週期預定支出的清單、新增、修改、刪除；掛在 <c>/books/{bookId}</c> 群組底下。</summary>
    public static RouteGroupBuilder MapRecurringPlannedExpensesEndpoints(this RouteGroupBuilder book)
    {
        book.MapGet("/recurring-planned-expenses", (Guid bookId, ISender sender, CancellationToken ct) =>
            sender.Send(new ListRecurringPlannedExpenses(bookId), ct));
        book.MapPost("/recurring-planned-expenses", async (Guid bookId, RecurringPlannedExpenseInput input, ISender sender, CancellationToken ct) =>
        {
            var dto = await sender.Send(new CreateRecurringPlannedExpense(bookId, input), ct);
            return Results.Created($"/api/books/{bookId}/recurring-planned-expenses/{dto.Id}", dto);
        });
        book.MapPut("/recurring-planned-expenses/{recurringId:guid}",
            (Guid bookId, Guid recurringId, UpdateRecurringPlannedExpenseBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new UpdateRecurringPlannedExpense(bookId, recurringId, body.Version, body.Input), ct));
        // DELETE 不帶 body，版本由 query string 的 ?version= 帶入（同預定支出）。
        book.MapDelete("/recurring-planned-expenses/{recurringId:guid}",
            async (Guid bookId, Guid recurringId, uint version, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new DeleteRecurringPlannedExpense(bookId, recurringId, version), ct);
                return Results.NoContent();
            });
        return book;
    }
}

/// <summary>修改週期預定支出的 body：讀取時拿到的版本，加上新的內容。</summary>
internal sealed record UpdateRecurringPlannedExpenseBody(uint Version, RecurringPlannedExpenseInput Input);
