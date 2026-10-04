using MediatR;
using SixJars.Application.Books;
using SixJars.Application.Common;

namespace SixJars.Application.Members;

/// <summary>目前登入的使用者與其所屬的帳本（<c>GET /api/me</c>）。不屬於任何帳本，所以不是 <see cref="IBookScoped"/>。</summary>
public sealed record GetMe : IRequest<MeDto>;

internal sealed class GetMeHandler(ISixJarsDbContext db, ICurrentUser user) : IRequestHandler<GetMe, MeDto>
{
    public async Task<MeDto> Handle(GetMe request, CancellationToken cancellationToken) =>
        new(user.Subject, user.Email, await db.ListMemberBooksAsync(user.Subject, cancellationToken));
}
