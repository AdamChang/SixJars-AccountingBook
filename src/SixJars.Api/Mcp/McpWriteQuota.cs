using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using SixJars.Api.Mcp.OAuth;
using SixJars.Application.Auditing;
using SixJars.Application.Common;

namespace SixJars.Api.Mcp;

/// <summary>
/// 每日寫入上限（spec D12、§6.4）：prompt injection 失控時的硬上限。
/// 計數來源是稽核記錄（「使用者＋經由」，今天台北 00:00 起的 Create 交易），所以重複的呼叫不寫稽核記錄、自然不佔額度。
/// 上限讀 <see cref="McpOAuthOptions"/>，與同意頁顯示的是同一個值。
/// </summary>
internal sealed class McpWriteQuota(ISixJarsDbContext db, IOptions<McpOAuthOptions> options, TimeProvider clock, ICurrentUser user)
{
    public async Task EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        var limit = options.Value.MaxWritesPerDay;
        var since = TaipeiTime.StartOfDayUtc(TaipeiTime.DateOf(clock.GetUtcNow()));
        var subject = user.Subject;
        var via = user.Via;

        var written = await db.AuditEntries.CountAsync(
            e => e.ActorSubject == subject && e.Via == via && e.Action == AuditAction.Create
                && e.EntityType == AuditEntityTypes.Transaction && e.At >= since,
            cancellationToken);

        if (written >= limit)
        {
            throw new McpException($"今天經由 Claude 記帳已達上限 {limit} 筆，明天再試或改在網頁上記帳。");
        }
    }
}
