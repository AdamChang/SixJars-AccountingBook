using Microsoft.AspNetCore.HttpOverrides;

namespace SixJars.Api.Infrastructure;

/// <summary>
/// Cloud Run 在 Google 的前端（GFE）終止 TLS，再以 http 轉給 container；原始的 scheme 與用戶端 IP 放在
/// <c>X-Forwarded-Proto</c>／<c>X-Forwarded-For</c>。不採用的話，OIDC 的 <c>redirect_uri</c> 會變成 <c>http://</c>（Google 拒絕），
/// antiforgery（SecurePolicy=Always）也會在每個請求擲例外而回 500。
/// </summary>
/// <remarks>
/// <para>
/// GFE 的 IP 不固定，所以清空 <see cref="ForwardedHeadersOptions.KnownProxies"/> 與 <see cref="ForwardedHeadersOptions.KnownIPNetworks"/>，
/// 信任任何來源的這兩個 header。安全上的取捨：
/// </para>
/// <list type="bullet">
/// <item>Cloud Run 的 container 只能經由 GFE 連到（沒有其他對外入口），GFE 會自己設定 <c>X-Forwarded-Proto</c>，用戶端偽造的值不會原樣到達。</item>
/// <item><c>X-Forwarded-For</c> 由 GFE 把真正的用戶端 IP 附加在最後；<see cref="ForwardedHeadersOptions.ForwardLimit"/> 維持預設的 1，
/// 只取最後一個值，用戶端自己塞在前面的假 IP 不會被採用。本系統沒有依 IP 做授權，IP 只用於記錄。</item>
/// <item>若日後改成可以不經 proxy 直接連到 app 的部署方式（例如 VM 對外開 port），必須改回只信任該 proxy 的 IP，
/// 否則任何人都能以 <c>X-Forwarded-Proto: https</c> 讓 app 誤以為連線已加密。</item>
/// </list>
/// </remarks>
internal static class ForwardedHeadersSetup
{
    public static void Configure(ForwardedHeadersOptions o)
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.KnownProxies.Clear();
        o.KnownIPNetworks.Clear();
    }
}
