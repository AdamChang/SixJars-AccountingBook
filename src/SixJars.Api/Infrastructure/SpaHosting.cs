using System.Text.RegularExpressions;
using Microsoft.Net.Http.Headers;

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
    private static readonly Regex HashedAssetPattern = new(@"-[A-Z2-7]{8}\.[a-z0-9]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Angular 的 lazy chunk 用大小寫混合的 8 字元雜湊，只對 chunk- 前綴放寬，避免 logo-20261004.png 這類自訂檔名被當成雜湊檔。
    private static readonly Regex ChunkAssetPattern = new(@"^chunk-[A-Za-z0-9_-]{8}\.js$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

        // 快取策略依「實際提供的檔名」決定，而不是依請求路徑：deep link 已被上面改寫成 /index.html，
        // 所以 /books/x/transactions 這類請求也會拿到 index.html 的 no-cache，不會被瀏覽器長期快取。
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                var cacheControl = new CacheControlHeaderValue();
                if (IsHashedAsset(context.File.Name))
                {
                    // 檔名含內容雜湊，內容變了檔名就變，可安心長期快取。
                    cacheControl.Public = true;
                    cacheControl.MaxAge = TimeSpan.FromDays(365);
                    cacheControl.Extensions.Add(new NameValueHeaderValue("immutable"));
                }
                else
                {
                    // index.html、ngsw.json、ngsw-worker.js 等入口與 service worker 檔必須每次向伺服器驗證，
                    // 否則使用者拿不到新版本（ngsw.json 若長期快取，PWA 更新會失效）。
                    cacheControl.NoCache = true;
                }

                context.Context.Response.GetTypedHeaders().CacheControl = cacheControl;
            },
        });
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

    /// <summary>
    /// 是否為 Angular build 產生的雜湊檔名（例如 main-DISDLN5L.js）：結尾為「-8 碼 base32 字元（A–Z、2–7）.副檔名」，或 lazy chunk（chunk-BvxS2djg.js，雜湊為大小寫混合的 8 字元）。
    /// </summary>
    /// <remarks>其他檔案刻意只收 base32 字母表（A–Z、2–7；esbuild 的內容雜湊格式）並區分大小寫：作者自己命名的 logo-20261004.png（含 0、1、8、9）或小寫的 main-abcd2345.js 都不是 build 產物，不應被當成 immutable 快取一年。</remarks>
    internal static bool IsHashedAsset(string fileName) => HashedAssetPattern.IsMatch(fileName) || ChunkAssetPattern.IsMatch(fileName);
}
