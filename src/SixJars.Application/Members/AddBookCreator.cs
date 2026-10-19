using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Common;
using SixJars.Domain.Common;
using SixJars.Domain.Members;

namespace SixJars.Application.Members;

/// <summary>
/// 把 email 加進建立者名單（CLI <c>add-creator</c>，ADR 0011），回傳 Id。尚未綁定 sub，第一次以這個 email 登入時才綁定。
/// 名單不屬於任何帳本，不留稽核記錄（issue #7 Q24）。CLI 專用，必須保持 internal。
/// </summary>
internal sealed record AddBookCreator(string Email) : IRequest<Guid>, ICliOnlyRequest;

internal sealed class AddBookCreatorValidator : AbstractValidator<AddBookCreator>
{
    public AddBookCreatorValidator() => RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(320);
}

internal sealed class AddBookCreatorHandler(ISixJarsDbContext db, TimeProvider clock) : IRequestHandler<AddBookCreator, Guid>
{
    public async Task<Guid> Handle(AddBookCreator request, CancellationToken cancellationToken)
    {
        var creator = BookCreator.Create(request.Email, clock.GetUtcNow());
        // 先查一次，給出看得懂的訊息；Email 的唯一索引仍是最後一道防線。
        if (await db.BookCreators.AnyAsync(c => c.Email == creator.Email, cancellationToken))
        {
            throw new DomainException($"{creator.Email} 已在建立者名單。");
        }

        db.BookCreators.Add(creator);
        await db.SaveChangesAsync(cancellationToken);
        return creator.Id;
    }
}
