using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Common;
using SixJars.Domain.Members;

namespace SixJars.Application.Members;

/// <summary>
/// 撤銷某個 email 的所有有效 MCP 授權（CLI <c>revoke-mcp-access</c>，ADR 0013），回傳撤銷的筆數。撤銷立即生效：
/// access token 每次都會檢查授權，refresh token 也換不到新的。不留稽核記錄（授權不屬於任何帳本，同 ADR 0011）。CLI 專用，必須保持 internal。
/// </summary>
/// <remarks>
/// 授權的 email 是授權當下的 claim，可能沒有，所以同時比對這個 email 已綁定的成員與建立者的 sub。
/// </remarks>
internal sealed record RevokeMcpAccess(string Email) : IRequest<int>, ICliOnlyRequest;

internal sealed class RevokeMcpAccessHandler(ISixJarsDbContext db, TimeProvider clock) : IRequestHandler<RevokeMcpAccess, int>
{
    public async Task<int> Handle(RevokeMcpAccess request, CancellationToken cancellationToken)
    {
        var email = BookMember.NormalizeEmail(request.Email);
        var subjects = await db.BookMembers.Where(m => m.Email == email && m.GoogleSubject != null).Select(m => m.GoogleSubject!)
            .Union(db.BookCreators.Where(c => c.Email == email && c.GoogleSubject != null).Select(c => c.GoogleSubject!))
            .ToListAsync(cancellationToken);
        var grants = await db.McpGrants
            .Where(g => g.RevokedAt == null && (subjects.Contains(g.Subject) || (g.Email != null && g.Email.ToLower() == email)))
            .ToListAsync(cancellationToken);

        var now = clock.GetUtcNow();
        foreach (var grant in grants)
        {
            grant.Revoke(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return grants.Count;
    }
}
