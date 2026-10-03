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
        return book;
    }
}
