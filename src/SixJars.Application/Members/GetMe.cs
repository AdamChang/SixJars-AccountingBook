using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Books;
using SixJars.Application.Common;

namespace SixJars.Application.Members;

/// <summary>目前登入的使用者與其所屬的帳本（<c>GET /api/me</c>）。不屬於任何帳本，所以不是 <see cref="IBookScoped"/>。</summary>
public sealed record GetMe : IRequest<MeDto>;

internal sealed class GetMeHandler(ISixJarsDbContext db, ICurrentUser user) : IRequestHandler<GetMe, MeDto>
{
    public async Task<MeDto> Handle(GetMe request, CancellationToken cancellationToken)
    {
        // 所屬的帳本就是身為擁有者的帳本（OwnedBookIds），直接拿來算剩餘名額，不再查一次。
        var books = await db.ListMemberBooksAsync(user.Subject, cancellationToken);
        var canCreate = await db.BookCreators.AnyAsync(c => c.GoogleSubject == user.Subject, cancellationToken);
        var remaining = canCreate ? Math.Max(0, CreateBook.MaxOwnedBooks - books.Count) : 0;
        return new MeDto(user.Subject, user.Email, books, canCreate, remaining);
    }
}
