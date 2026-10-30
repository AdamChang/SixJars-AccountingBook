using MediatR;
using SixJars.Application.Common;

namespace SixJars.Application.Books;

public sealed record GetBook(Guid BookId) : IRequest<BookDto>, IBookScoped;

internal sealed class GetBookHandler(IBookReader books) : IRequestHandler<GetBook, BookDto>
{
    public async Task<BookDto> Handle(GetBook request, CancellationToken cancellationToken)
    {
        var book = await books.GetReadOnlyBookAsync(request.BookId, cancellationToken);
        return BookDto.From(book);
    }
}
