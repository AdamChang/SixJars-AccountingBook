using SixJars.Application.Common;

namespace SixJars.Api.Infrastructure;

/// <summary>從目前 HTTP 請求的 claim 讀取登入身分（Google 的 <c>sub</c> 與 <c>email</c>）。</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string Subject =>
        accessor.HttpContext?.User.FindFirst("sub")?.Value ?? throw new InvalidOperationException("目前的請求沒有登入身分（缺少 sub claim）。");

    public string? Email => accessor.HttpContext?.User.FindFirst("email")?.Value;

    /// <summary>只有 MCP bearer token 認證會帶 <c>via</c> claim（<c>mcp:&lt;client host&gt;</c>）；cookie 登入沒有，所以是 null。</summary>
    public string? Via => accessor.HttpContext?.User.FindFirst("via")?.Value;
}
