using MediatR;
using SixJars.Application.Books;

namespace SixJars.Api.Endpoints;

internal static class BooksEndpoints
{
    /// <summary>帳本清單與帳本設定；回傳 <c>/books/{bookId}</c> 群組，讓其他 endpoint 掛在底下。</summary>
    public static RouteGroupBuilder MapBooksEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/books", (ISender sender, CancellationToken ct) => sender.Send(new ListBooks(), ct));
        var book = api.MapGroup("/books/{bookId:guid}");
        book.MapGet("/", (Guid bookId, ISender sender, CancellationToken ct) => sender.Send(new GetBook(bookId), ct));

        // 帳本 Id 一律以路由為準，覆寫 body 裡的值。
        book.MapPost("/accounts", async (Guid bookId, AddAccount command, ISender sender, CancellationToken ct) =>
            Created(bookId, await sender.Send(command with { BookId = bookId }, ct)));
        book.MapPost("/planning-funds", async (Guid bookId, AddPlanningFund command, ISender sender, CancellationToken ct) =>
            Created(bookId, await sender.Send(command with { BookId = bookId }, ct)));
        book.MapPost("/categories", async (Guid bookId, AddCategory command, ISender sender, CancellationToken ct) =>
            Created(bookId, await sender.Send(command with { BookId = bookId }, ct)));
        // lockDate 為 null 時清除鎖帳日。
        book.MapPut("/lock-date", async (Guid bookId, SetLockDateBody body, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new SetLockDate(bookId, body.LockDate), ct);
            return Results.NoContent();
        });
        return book;
    }

    private static IResult Created(Guid bookId, Guid id) => Results.Created($"/api/books/{bookId}", new { id });
}

/// <summary>設定鎖帳日的 body；<see cref="LockDate"/> 為 null 時清除。</summary>
internal sealed record SetLockDateBody(DateOnly? LockDate);
