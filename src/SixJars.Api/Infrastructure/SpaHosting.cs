namespace SixJars.Api.Infrastructure;

/// <summary>
/// 由 Api 直接提供 Angular build 輸出（wwwroot）與 SPA 的 index.html fallback。
/// </summary>
/// <remarks>
/// fallback 刻意用「改寫路徑」的 middleware，而不是 <c>MapFallbackToFile</c>：後者會在 EndpointDataSource 多出一個
/// 沒有 HttpMethodMetadata 的匿名 endpoint，違反 SecurityConventionTests 的匿名白名單（只允許 /health 與 /auth/**）。
/// 改寫路徑不新增 endpoint，安全慣例不必放寬。
/// </remarks>
public static class SpaHosting
{
    private static readonly string[] BackendPrefixes = ["api", "auth", "health"];

    public static WebApplication UseSpaHosting(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            // 只改寫沒有被任何 endpoint 接走的 GET/HEAD；POST 等其他方法維持原本的 404/405，不能被吞成 HTML。
            if (context.GetEndpoint() is null
                && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
                && IsFrontendRoute(context.Request.Path)
                && app.Environment.WebRootFileProvider.GetFileInfo("index.html").Exists)
            {
                context.Request.Path = "/index.html";
            }

            await next();
        });

        // 靜態檔放在這一個地方，之後要加快取 header 只需在此補上 StaticFileOptions。
        app.UseStaticFiles();
        return app;
    }

    /// <summary>
    /// 是否為前端（Angular router）的路徑：第一段不是 api／auth／health，且最後一段沒有副檔名。
    /// </summary>
    /// <remarks>
    /// 比對的是「整段」：/apix 屬於前端，/api/nope 與 /api 不是，打錯的 API 路徑必須維持 404 而不是 200 的 HTML。
    /// 有副檔名的路徑是檔案請求，缺檔時應該 404（舊的雜湊檔名不能回 index.html，否則瀏覽器會把 HTML 當成 JS 執行）。
    /// </remarks>
    internal static bool IsFrontendRoute(PathString path)
    {
        var segments = (path.Value ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return true;
        }

        return !BackendPrefixes.Contains(segments[0], StringComparer.OrdinalIgnoreCase)
            && !segments[^1].Contains('.');
    }
}
