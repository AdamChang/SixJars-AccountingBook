using MediatR;
using SixJars.Application.Planning;

namespace SixJars.Api.Endpoints;

internal static class PlannedExpensesEndpoints
{
    /// <summary>預定支出的新增、清單、讀取單筆、修改、刪除與付款；掛在 <c>/books/{bookId}</c> 群組底下。</summary>
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
        // 明確的產生動作（ADR 0009）；路徑不是 guid，不會和 /{plannedExpenseId:guid} 衝突。
        book.MapPost("/planned-expenses/generate", (Guid bookId, int budgetMonth, ISender sender, CancellationToken ct) =>
            sender.Send(new GeneratePlannedExpenses(bookId, budgetMonth), ct));
        book.MapPost("/planned-expenses/refresh", (Guid bookId, int budgetMonth, ISender sender, CancellationToken ct) =>
            sender.Send(new RefreshPlannedExpenses(bookId, budgetMonth), ct));
        book.MapGet("/planned-expenses/{plannedExpenseId:guid}", (Guid bookId, Guid plannedExpenseId, ISender sender, CancellationToken ct) =>
            sender.Send(new GetPlannedExpense(bookId, plannedExpenseId), ct));
        book.MapPut("/planned-expenses/{plannedExpenseId:guid}",
            (Guid bookId, Guid plannedExpenseId, UpdatePlannedExpenseBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new UpdatePlannedExpense(bookId, plannedExpenseId, body.Version, body.Input), ct));
        // DELETE 不帶 body，版本由 query string 的 ?version= 帶入。
        book.MapDelete("/planned-expenses/{plannedExpenseId:guid}",
            async (Guid bookId, Guid plannedExpenseId, uint version, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new DeletePlannedExpense(bookId, plannedExpenseId, version), ct);
                return Results.NoContent();
            });
        book.MapPost("/planned-expenses/{plannedExpenseId:guid}/pay",
            async (Guid bookId, Guid plannedExpenseId, PayPlannedExpenseBody body, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new PayPlannedExpense(
                    bookId, plannedExpenseId, body.Version, body.Date, body.AccountId, body.Amount, body.LoanAccountId, body.LoanPrincipal), ct);
                return Results.Created($"/api/books/{bookId}/transactions/{result.Transaction.Id}", result);
            });
        return book;
    }
}

/// <summary>預定支出付款的 body；欄位說明見 <see cref="PayPlannedExpense"/>。</summary>
internal sealed record PayPlannedExpenseBody(uint Version, DateOnly Date, Guid AccountId, decimal Amount, Guid? LoanAccountId, decimal? LoanPrincipal);

/// <summary>修改預定支出的 body：讀取時拿到的版本，加上新的內容。</summary>
internal sealed record UpdatePlannedExpenseBody(uint Version, PlannedExpenseInput Input);
