using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Common;
using SixJars.Domain.Common;

namespace SixJars.Application.Books;

public sealed record GetBook(Guid BookId) : IRequest<BookDto>, IBookScoped;

internal sealed class GetBookHandler(ISixJarsDbContext db) : IRequestHandler<GetBook, BookDto>
{
    public async Task<BookDto> Handle(GetBook request, CancellationToken cancellationToken)
    {
        var bookId = new BookId(request.BookId);
        var book = await db.Books.AsNoTracking().SingleOrDefaultAsync(b => b.Id == bookId, cancellationToken)
            ?? throw new NotFoundException($"找不到帳本 {request.BookId}。");
        return BookDto.From(book);
    }
}
