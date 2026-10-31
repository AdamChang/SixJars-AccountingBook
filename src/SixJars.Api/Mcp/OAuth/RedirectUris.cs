namespace SixJars.Api.Mcp.OAuth;

/// <summary>redirect_uri 是否在 client 文件登記的清單裡（ADR 0013）。</summary>
internal static class RedirectUris
{
    /// <summary>
    /// 一般網址逐字比對，不做任何正規化（多一個斜線、預設 port 寫出來都算不同）：比對越寬鬆，越容易被拿來把 code 送到別處。
    /// 只有登記值是 http 的 loopback（localhost、127.0.0.1、[::1]）時忽略 port：Claude Code 每次用不同的臨時 port（RFC 8252 §7.3）；
    /// scheme、host、path、query 仍要完全相同。
    /// </summary>
    public static bool IsRegistered(string requested, IEnumerable<string> registered)
    {
        if (!Uri.TryCreate(requested, UriKind.Absolute, out var requestedUri))
        {
            return false;
        }

        return registered.Any(candidate => candidate == requested
            || (Uri.TryCreate(candidate, UriKind.Absolute, out var candidateUri) && IsSameLoopbackIgnoringPort(requestedUri, candidateUri)));
    }

    private static bool IsSameLoopbackIgnoringPort(Uri requested, Uri registered) =>
        registered.Scheme == Uri.UriSchemeHttp
        && requested.Scheme == Uri.UriSchemeHttp
        && IsLoopbackHost(registered.Host)
        && string.Equals(requested.Host, registered.Host, StringComparison.OrdinalIgnoreCase)
        && requested.PathAndQuery == registered.PathAndQuery
        && requested.UserInfo.Length == 0
        && requested.Fragment.Length == 0;

    private static bool IsLoopbackHost(string host) => host is "localhost" or "127.0.0.1" or "[::1]";
}
