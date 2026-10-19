using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Auditing;
using SixJars.Application.Common;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Members;

namespace SixJars.Application.Books;

/// <summary>
/// 建立者建立一本空白帳本，自己成為擁有者（issue #7、ADR 0011），回傳帳本 Id。
/// 沒有 BookId，所以不是 <see cref="IBookScoped"/>；權限在 handler 內檢查：不在建立者名單擲 <see cref="ForbiddenException"/>（403）。
/// </summary>
public sealed record CreateBook(string Name, DateOnly OpeningDate) : IRequest<Guid>
{
    /// <summary>每人最多擁有的帳本數（Q8）：計算目前身為擁有者的帳本，刪除後名額就空出來。CLI 不受此限。</summary>
    public const int MaxOwnedBooks = 5;
}

/// <summary>建立帳本的稽核快照（Q19）。</summary>
public sealed record CreatedBookSnapshot(string Name, DateOnly OpeningDate);

internal sealed class CreateBookValidator : AbstractValidator<CreateBook>
{
    public CreateBookValidator()
    {
        // 與 BookConfiguration 的名稱長度一致。
        RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
        // 沒帶 openingDate 時 JSON 會綁成 default(DateOnly)（0001-01-01），視為缺漏而非有效開帳日。
        RuleFor(c => c.OpeningDate).NotEqual(default(DateOnly));
    }
}

internal sealed class CreateBookHandler(ISixJarsDbContext db, ICurrentUser user, IAuditTrail audit, TimeProvider clock)
    : IRequestHandler<CreateBook, Guid>
{
    public async Task<Guid> Handle(CreateBook request, CancellationToken cancellationToken)
    {
        // Q16：先鎖住這位建立者再計數，同時送出的兩個請求不會都看到 4 本。鎖到 commit／rollback 才釋放。
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        await db.AcquireTransactionLockAsync($"create-book:{user.Subject}", cancellationToken);

        if (!await db.BookCreators.AnyAsync(c => c.GoogleSubject == user.Subject, cancellationToken))
        {
            throw new ForbiddenException("你不在建立者名單，無法建立帳本。");
        }

        var owned = db.OwnedBookIds(user.Subject);
        if (await owned.CountAsync(cancellationToken) >= CreateBook.MaxOwnedBooks)
        {
            throw new DomainException(
                $"每人最多擁有 {CreateBook.MaxOwnedBooks} 本帳本，已達上限。", DomainException.BookQuotaExceededCode);
        }

        var book = new Book(request.Name, request.OpeningDate);
        if (await db.OwnerHasBookNamedAsync(owned, book.Name, cancellationToken))
        {
            throw new DomainException($"你已經有名稱為「{book.Name}」的帳本。", DomainException.DuplicateNameCode);
        }

        // D7：建立者一定是以 email 綁定進來的；沒有 email 的身分不能成為成員（NormalizeEmail 的前提）。
        var email = user.Email ?? throw new ForbiddenException("登入身分沒有 email，無法建立帳本。");
        var owner = BookMember.Create(book.Id, email, BookRole.Owner, clock.GetUtcNow());
        // 立即綁定：/api/me 與 BookAccessBehavior 都以 sub 認人。
        owner.BindSubject(user.Subject);

        db.Books.Add(book);
        db.BookMembers.Add(owner);
        audit.Record<CreatedBookSnapshot>(book.Id.Value, AuditAction.Create, AuditEntityTypes.Book, book.Id.Value,
            null, new CreatedBookSnapshot(book.Name, book.OpeningDate));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return book.Id.Value;
    }
}
