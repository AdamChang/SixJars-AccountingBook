using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Auditing;
using SixJars.Application.Common;
using SixJars.Domain.Common;
using SixJars.Domain.Members;

namespace SixJars.Application.Members;

/// <summary>
/// 把 email 加為帳本的擁有者（CLI <c>add-member</c>，spec §8.1），回傳新成員的 Id。成員尚未綁定 Google sub，
/// 第一次以這個 email 登入時才綁定（T36）。
/// CLI 是管理工具，操作者不是任何帳本的成員，所以是 <see cref="ICliOnlyRequest"/> 而不是 <see cref="IBookScoped"/>；
/// 必須保持 internal，API 才無法送出（見 <see cref="ICliOnlyRequest"/>）。
/// </summary>
internal sealed record AddOwner(Guid BookId, string Email) : IRequest<Guid>, ICliOnlyRequest;

internal sealed class AddOwnerValidator : AbstractValidator<AddOwner>
{
    public AddOwnerValidator()
    {
        // RFC 5321 的 email 上限，與 BookMemberConfiguration 一致。
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(320);
    }
}

internal sealed class AddOwnerHandler(ISixJarsDbContext db, IAuditTrail audit, TimeProvider clock) : IRequestHandler<AddOwner, Guid>
{
    public async Task<Guid> Handle(AddOwner request, CancellationToken cancellationToken)
    {
        var bookId = new BookId(request.BookId);
        if (!await db.Books.AnyAsync(b => b.Id == bookId, cancellationToken))
        {
            throw new NotFoundException($"找不到帳本 {request.BookId}。");
        }

        var member = BookMember.Create(bookId, request.Email, BookRole.Owner, clock.GetUtcNow());
        // 先查一次，給出看得懂的訊息；(BookId, Email) 的唯一索引仍是最後一道防線（同時執行兩次的極端情況）。
        if (await db.BookMembers.AnyAsync(m => m.BookId == bookId && m.Email == member.Email, cancellationToken))
        {
            throw new DomainException($"{member.Email} 已是帳本 {request.BookId} 的成員。");
        }

        db.BookMembers.Add(member);
        audit.Record(request.BookId, AuditAction.Create, AuditEntityTypes.BookMember, member.Id, null, BookMemberDto.From(member));
        await db.SaveChangesAsync(cancellationToken);
        return member.Id;
    }
}
