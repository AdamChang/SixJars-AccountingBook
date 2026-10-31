using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;

namespace SixJars.Api.Mcp.OAuth;

/// <summary>
/// 授權同意頁與錯誤頁：伺服器端產生的簡單 HTML，不經過 Angular（授權流程在 Claude 開的彈出視窗裡，SPA 的登入狀態與路由都不相干）。
/// </summary>
/// <remarks>
/// 顯示的是 client_id 的 host 而不是 CIMD 文件的 client_name（文件內容是 client 自己宣稱的），並列出轉址目的地的 host（MCP 規範要求）。
/// frame-ancestors 'none' 防止同意按鈕被嵌進別的網站騙點擊；不設 form-action，因為 Chrome 會把同意後轉址到 claude.ai 也算進去。
/// </remarks>
internal static class ConsentPage
{
    public static IResult Render(HttpContext context, IAntiforgery antiforgery, AuthorizeValidation.Valid request, string? email)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        var redirectHost = new Uri(request.RedirectUri).Authority;
        var html = new StringBuilder()
            .Append("<h1>允許存取六罐子記帳本？</h1>")
            .Append("<p><strong>").Append(Encode(request.ClientHost)).Append("</strong> 要求以你的身分（")
            .Append(Encode(email ?? "目前登入的帳號")).Append("）<strong>唯讀</strong>存取你可以看到的帳本、帳戶與交易。</p>")
            .Append("<p>同意後會轉回 <code>").Append(Encode(redirectHost)).Append("</code>。它不能新增、修改或刪除任何資料；之後可由管理者撤銷。</p>")
            // 授權釣魚：攻擊者可以把自己 Claude 產生的授權連結傳給你，同意後就是「他的」Claude 讀你的帳本；技術上無法分辨，只能提醒。
            .Append("<p style=\"border:2px solid #c62828;padding:.75rem;background:#ffebee\"><strong>只有在你剛剛自己從 Claude 按下「Connect」時才允許。</strong>")
            .Append("別人傳給你的連結請按「拒絕」：同意後，存取權會交給發起這個連結的人的 Claude，對方就能讀你的帳本。</p>")
            .Append("<form method=\"post\" action=\"").Append(OAuthEndpoints.AuthorizePath).Append("\">")
            .Append(Hidden(tokens.FormFieldName, tokens.RequestToken!))
            .Append(Hidden("response_type", "code"))
            .Append(Hidden("client_id", request.ClientId))
            .Append(Hidden("redirect_uri", request.RedirectUri))
            .Append(Hidden("code_challenge", request.CodeChallenge))
            .Append(Hidden("code_challenge_method", "S256"))
            .Append(Hidden("scope", request.Scope))
            .Append(Hidden("resource", request.Resource))
            .Append(request.State is null ? "" : Hidden("state", request.State))
            .Append("<button type=\"submit\" name=\"decision\" value=\"allow\">允許</button> ")
            .Append("<button type=\"submit\" name=\"decision\" value=\"deny\">拒絕</button>")
            .Append("</form>");
        return Page(context, "授權存取", html.ToString(), StatusCodes.Status200OK);
    }

    public static IResult Error(HttpContext context, string message) =>
        Page(context, "無法授權", $"<h1>無法授權</h1><p>{Encode(message)}</p>", StatusCodes.Status400BadRequest);

    private static IResult Page(HttpContext context, string title, string body, int statusCode)
    {
        context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'none'";
        context.Response.Headers.CacheControl = "no-store";
        var html = $"""
            <!doctype html>
            <html lang="zh-Hant"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{Encode(title)}</title>
            <style>body{"{"}font-family:system-ui,sans-serif;max-width:32rem;margin:3rem auto;padding:0 1rem;line-height:1.6{"}"}button{"{"}font-size:1rem;padding:.5rem 1.25rem{"}"}</style>
            </head><body>{body}</body></html>
            """;
        return Results.Content(html, "text/html", Encoding.UTF8, statusCode);
    }

    private static string Hidden(string name, string value) => $"<input type=\"hidden\" name=\"{Encode(name)}\" value=\"{Encode(value)}\">";

    private static string Encode(string value) => HtmlEncoder.Default.Encode(value);
}
