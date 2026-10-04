using MediatR;
using SixJars.Application.Auditing;
using SixJars.Application.Common;

namespace SixJars.Application.Books;

/// <summary>
/// 設定或清除（<see cref="LockDate"/> 為 null）鎖帳日；可前移、後移（spec §9 O3），每次變更都留下稽核記錄。
/// 帳本沒有樂觀並行版本，同時設定時以最後一次為準。
/// </summary>
public sealed record SetLockDate(Guid BookId, DateOnly? LockDate) : IRequest, IBookScoped;

/// <summary>鎖帳日的稽核快照：<c>{ lockDate }</c>。</summary>
public sealed record LockDateSnapshot(DateOnly? LockDate);

internal sealed class SetLockDateHandler(ISixJarsDbContext db, IAuditTrail audit) : IRequestHandler<SetLockDate>
{
    public async Task Handle(SetLockDate request, CancellationToken cancellationToken)
    {
        var book = await db.GetBookForUpdateAsync(request.BookId, cancellationToken);

        // 修改前的快照必須在 SetLockDate 之前取得。
        var before = new LockDateSnapshot(book.LockDate);
        book.SetLockDate(request.LockDate);
        audit.Record(request.BookId, AuditAction.LockDateChanged, AuditEntityTypes.Book, request.BookId,
            before, new LockDateSnapshot(book.LockDate));
        await db.SaveChangesAsync(cancellationToken);
    }
}
