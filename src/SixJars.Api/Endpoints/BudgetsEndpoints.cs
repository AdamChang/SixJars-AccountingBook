using MediatR;
using SixJars.Application.Planning;

namespace SixJars.Api.Endpoints;

internal static class BudgetsEndpoints
{
    /// <summary>預算的設定與清除（spec §5.2）；掛在 <c>/books/{bookId}</c> 群組底下。查詢在 L6 加上。</summary>
    public static RouteGroupBuilder MapBudgetsEndpoints(this RouteGroupBuilder book)
    {
        book.MapPut("/budgets/{categoryId:guid}/default",
            (Guid bookId, Guid categoryId, BudgetAmountBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new SetDefaultBudget(bookId, categoryId, body.Amount), ct));
        book.MapDelete("/budgets/{categoryId:guid}/default", async (Guid bookId, Guid categoryId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new RemoveDefaultBudget(bookId, categoryId), ct);
            return Results.NoContent();
        });
        book.MapPut("/budgets/{categoryId:guid}/overrides/{budgetMonth:int}",
            (Guid bookId, Guid categoryId, int budgetMonth, BudgetAmountBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new SetBudgetOverride(bookId, categoryId, budgetMonth, body.Amount), ct));
        // DELETE 刪到整筆消失也是 204（P4 L plan D5）。
        book.MapDelete("/budgets/{categoryId:guid}/overrides/{budgetMonth:int}",
            async (Guid bookId, Guid categoryId, int budgetMonth, ISender sender, CancellationToken ct) =>
            {
                await sender.Send(new RemoveBudgetOverride(bookId, categoryId, budgetMonth), ct);
                return Results.NoContent();
            });
        return book;
    }
}

/// <summary>PUT 預設值與覆寫值的 body（P4 L plan D5）。</summary>
internal sealed record BudgetAmountBody(decimal Amount);
