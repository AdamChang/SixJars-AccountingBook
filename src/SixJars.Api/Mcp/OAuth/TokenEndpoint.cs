using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Common;
using SixJars.Domain.Members;

namespace SixJars.Api.Mcp.OAuth;

/// <summary>
/// /oauth/token（ADR 0013）：authorization_code 與 refresh_token 兩種 grant，public client（不收 client secret，靠 PKCE）。
/// </summary>
/// <remarks>
/// body 只接受 form-urlencoded（RFC 6749 §4.1.3）。不掛 antiforgery：這是 Claude 伺服器端的呼叫，不會帶 cookie。
/// 所有失敗都回 400 加 RFC 6749 的錯誤碼，不說明是哪個欄位不符；Claude 看到失效的 refresh 回 <c>invalid_grant</c> 才會要求重新連線。
/// </remarks>
internal static class TokenEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext context, McpTokens tokens, ISixJarsDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!context.Request.HasFormContentType)
        {
            return Error("invalid_request", "body 必須是 application/x-www-form-urlencoded。");
        }

        var form = await context.Request.ReadFormAsync(cancellationToken);
        return form["grant_type"].ToString() switch
        {
            "authorization_code" => await ExchangeCodeAsync(form, tokens, db, clock.GetUtcNow(), cancellationToken),
            "refresh_token" => await RefreshAsync(form, context.Request, tokens, db, clock.GetUtcNow(), cancellationToken),
            _ => Error("unsupported_grant_type", "只支援 authorization_code 與 refresh_token。"),
        };
    }

    /// <summary>
    /// code 必須未過期，並與授權時的 client、redirect_uri、resource 一致，code_verifier 也要通過 PKCE 檢查。
    /// 同一個 code 第二次兌換代表外洩：拒絕，並撤銷第一次兌換建立的授權（RFC 6749 §4.1.2）。
    /// </summary>
    private static async Task<IResult> ExchangeCodeAsync(
        IFormCollection form, McpTokens tokens, ISixJarsDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var code = tokens.ReadAuthorizationCode(form["code"].ToString());
        if (code is null
            || form["client_id"] != code.ClientId
            || form["redirect_uri"] != code.RedirectUri
            || (form.ContainsKey("resource") && form["resource"] != code.Resource)
            || !VerifiesPkce(form["code_verifier"].ToString(), code.CodeChallenge))
        {
            return InvalidGrant();
        }

        var redeemed = await db.McpGrants.SingleOrDefaultAsync(g => g.AuthorizationCodeId == code.Id, cancellationToken);
        if (redeemed is not null)
        {
            redeemed.Revoke(now);
            await db.SaveChangesAsync(cancellationToken);
            return InvalidGrant();
        }

        var refreshToken = McpTokens.NewRefreshToken();
        var grant = McpGrant.Issue(code.Subject, code.Email, code.ClientId, code.Scope, code.Id, McpTokens.Hash(refreshToken), now,
            McpOAuth.RefreshTokenLifetime);
        db.McpGrants.Add(grant);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // 兩個請求同時兌換同一個 code：AuthorizationCodeId 的 unique index 讓後到的失敗。
            return InvalidGrant();
        }

        return Issued(tokens.CreateAccessToken(grant, code.Resource, now), refreshToken, grant.Scope);
    }

    /// <summary>以 hash 找授權（現在或上一個 token 都要找得到，才分辨得出重放）；輪替後發新的 access 與 refresh。</summary>
    private static async Task<IResult> RefreshAsync(
        IFormCollection form, HttpRequest request, McpTokens tokens, ISixJarsDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var presentedHash = McpTokens.Hash(form["refresh_token"].ToString());
        var grant = await db.McpGrants.FirstOrDefaultAsync(
            g => g.RefreshTokenHash == presentedHash || g.PreviousRefreshTokenHash == presentedHash, cancellationToken);
        if (grant is null || (form.ContainsKey("client_id") && form["client_id"] != grant.ClientId))
        {
            return InvalidGrant();
        }

        var refreshToken = McpTokens.NewRefreshToken();
        var outcome = grant.Refresh(presentedHash, McpTokens.Hash(refreshToken), now, McpOAuth.RefreshTokenLifetime);
        // 重放時 Refresh 已撤銷授權，同樣要寫入。
        await db.SaveChangesAsync(cancellationToken);
        return outcome == McpRefreshOutcome.Rotated
            ? Issued(tokens.CreateAccessToken(grant, McpOAuth.Resource(request), now), refreshToken, grant.Scope)
            : InvalidGrant();
    }

    /// <summary>S256：BASE64URL(SHA256(ASCII(code_verifier))) 必須等於 code_challenge；以固定時間比較。</summary>
    private static bool VerifiesPkce(string verifier, string challenge)
    {
        if (string.IsNullOrEmpty(verifier))
        {
            return false;
        }

        var computed = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(challenge));
    }

    private static IResult Issued(string accessToken, string refreshToken, string scope) => Results.Json(new TokenResponse(
        accessToken, "Bearer", (int)McpOAuth.AccessTokenLifetime.TotalSeconds, refreshToken, scope));

    private static IResult InvalidGrant() => Error("invalid_grant", "授權碼或 refresh token 無效、已過期或已被撤銷。");

    private static IResult Error(string error, string description) =>
        Results.Json(new TokenError(error, description), statusCode: StatusCodes.Status400BadRequest);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("token_type")] string TokenType,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("scope")] string Scope);

    private sealed record TokenError(
        [property: JsonPropertyName("error")] string Error,
        [property: JsonPropertyName("error_description")] string ErrorDescription);
}
