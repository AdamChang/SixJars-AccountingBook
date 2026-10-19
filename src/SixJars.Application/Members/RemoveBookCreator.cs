using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Common;
using SixJars.Domain.Members;

namespace SixJars.Application.Members;

/// <summary>
/// 從建立者名單移除 email（CLI <c>remove-creator</c>，ADR 0011）。已建立的帳本不受影響，既有 cookie 也不會立即失效（issue #7 Q20）。
/// 不留稽核記錄（Q24）。CLI 專用，必須保持 internal。
/// </summary>
internal sealed record RemoveBookCreator(string Email) : IRequest, ICliOnlyRequest;

internal sealed class RemoveBookCreatorHandler(ISixJarsDbContext db) : IRequestHandler<RemoveBookCreator>
{
    public async Task Handle(RemoveBookCreator request, CancellationToken cancellationToken)
    {
        var email = BookMember.NormalizeEmail(request.Email);
        var creator = await db.BookCreators.SingleOrDefaultAsync(c => c.Email == email, cancellationToken)
            ?? throw new NotFoundException($"{email} 不在建立者名單。");
        db.BookCreators.Remove(creator);
        await db.SaveChangesAsync(cancellationToken);
    }
}
