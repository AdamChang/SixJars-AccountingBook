using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Auditing;
using SixJars.Application.Common;
using SixJars.Application.Members;
using SixJars.Domain.Members;

namespace SixJars.Api.Infrastructure;

/// <summary>
/// Google 登入的白名單檢查（ADR 0005、spec §3.4），由 OIDC 的 <c>OnTokenValidated</c> 呼叫；回傳 false 就拒絕登入、不發 cookie。
/// 只依賴 claim，不連 Google，測試直接以 <see cref="ClaimsPrincipal"/> 呼叫。
/// </summary>
internal sealed class GoogleSignInValidator(ISixJarsDbContext db, IAuditTrail audit)
{
    /// <summary>
    /// 1. 必須有 <c>sub</c>，而且 <c>email_verified</c> 為 true（缺少就拒絕）：未驗證的 email 可以是任何人填的。
    /// 2. 以正規化的 email 找出尚未綁定的成員，全部綁定這個 sub，每筆各留一筆稽核記錄（操作者就是這個 sub）。
    ///    已綁定其他 sub 的成員不在比對範圍內：換了 Google 帳號不能沿用同一個 email 取得存取權，也不會因為
    ///    <see cref="BookMember.BindSubject"/> 擲例外而在 callback 變成 500。
    /// 3. 最後只要有任何成員綁定這個 sub 就通過；之後以 sub 辨識，Google 帳號改 email 也不受影響。
    /// </summary>
    /// <remarks>
    /// 計畫原本是「sub 已綁定就直接通過」，但這樣已經登入過的人之後被 CLI 加進另一本帳，那本帳的成員永遠不會被綁定；
    /// 所以先綁定、再判斷。
    /// </remarks>
    public async Task<bool> ValidateAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var subject = principal.FindFirst("sub")?.Value;
        var emailVerified = principal.FindFirst("email_verified")?.Value;
        if (string.IsNullOrWhiteSpace(subject) || !string.Equals(emailVerified, "true", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var email = principal.FindFirst("email")?.Value;
        if (!string.IsNullOrWhiteSpace(email))
        {
            await BindUnboundMembersAsync(BookMember.NormalizeEmail(email), subject, cancellationToken);
        }

        return await db.BookMembers.AnyAsync(m => m.GoogleSubject == subject, cancellationToken);
    }

    private async Task BindUnboundMembersAsync(string email, string subject, CancellationToken cancellationToken)
    {
        var unbound = await db.BookMembers
            .Where(m => m.Email == email && m.GoogleSubject == null)
            .ToListAsync(cancellationToken);
        if (unbound.Count == 0)
        {
            return;
        }

        foreach (var member in unbound)
        {
            var before = BookMemberDto.From(member);
            member.BindSubject(subject);
            audit.RecordAs(subject, member.BookId.Value, AuditAction.Update, AuditEntityTypes.BookMember, member.Id,
                before, BookMemberDto.From(member));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
