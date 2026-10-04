using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Domain.Common;

namespace SixJars.Application.Common;

/// <summary>
/// 帳本範圍的 request（<see cref="IBookScoped"/>）一律先檢查登入者是否為擁有者；不是就當作不存在
/// （404，不透露帳本存在與否，ADR 0005）。每個帳本範圍的 request 因此多一次資料庫往返。
/// 註冊在 <see cref="ValidationBehavior{TRequest, TResponse}"/> 之前，非成員送出不合法的內容也只會拿到 404。
/// </summary>
public sealed class BookAccessBehavior<TRequest, TResponse>(ISixJarsDbContext db, ICurrentUser user)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is IBookScoped scoped)
        {
            var bookId = new BookId(scoped.BookId);
            var allowed = await db.OwnedBookIds(user.Subject).AnyAsync(id => id == bookId, cancellationToken);
            if (!allowed)
            {
                throw new NotFoundException($"找不到帳本 {scoped.BookId}。");
            }
        }

        return await next(cancellationToken);
    }
}
