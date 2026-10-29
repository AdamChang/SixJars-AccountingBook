using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using SixJars.Domain.Members;

namespace SixJars.Api.Mcp.OAuth;

/// <summary>
/// authorization code 與 access token 的產生和驗證：以 DataProtection（金鑰已存 DB，ADR 0005）加密的自包含 payload。
/// refresh token 是亂數，DB 只存 SHA-256 hash（<see cref="McpGrant"/>）。
/// </summary>
/// <remarks>
/// 到期時間寫進 payload、以注入的 <see cref="TimeProvider"/> 判斷，而不是用 time-limited protector：
/// 後者讀系統時鐘，測試無法讓 code 過期。不用 JWT：沒有第三方需要驗證，簽章金鑰管理是多餘的（ADR 0013）。
/// </remarks>
internal sealed class McpTokens(IDataProtectionProvider dataProtection, TimeProvider clock)
{
    private readonly IDataProtector codeProtector = dataProtection.CreateProtector("SixJars.Mcp.AuthorizationCode");
    private readonly IDataProtector accessProtector = dataProtection.CreateProtector("SixJars.Mcp.AccessToken");

    public string CreateAuthorizationCode(AuthorizationCode code) => Protect(codeProtector, code);

    /// <summary>解不開、格式不對或已過期時回傳 null；呼叫端一律回 <c>invalid_grant</c>，不區分原因。</summary>
    public AuthorizationCode? ReadAuthorizationCode(string value)
    {
        var code = Unprotect<AuthorizationCode>(codeProtector, value);
        return code is not null && clock.GetUtcNow() < code.ExpiresAt ? code : null;
    }

    public string CreateAccessToken(McpGrant grant, string audience, DateTimeOffset now) =>
        Protect(accessProtector, new AccessToken(grant.Id, grant.Subject, grant.Email, grant.Scope, audience, now + McpOAuth.AccessTokenLifetime));

    /// <summary>解不開或已過期時回傳 null；audience 與授權是否撤銷由 <see cref="McpBearerHandler"/> 檢查。</summary>
    public AccessToken? ReadAccessToken(string value)
    {
        var token = Unprotect<AccessToken>(accessProtector, value);
        return token is not null && clock.GetUtcNow() < token.ExpiresAt ? token : null;
    }

    /// <summary>256 位元亂數，base64url。</summary>
    public static string NewRefreshToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string refreshToken) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    private static string Protect<T>(IDataProtector protector, T payload) =>
        WebEncoders.Base64UrlEncode(protector.Protect(JsonSerializer.SerializeToUtf8Bytes(payload)));

    private static T? Unprotect<T>(IDataProtector protector, string value) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(protector.Unprotect(WebEncoders.Base64UrlDecode(value)));
        }
        catch (Exception e) when (e is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }
}

/// <summary>authorization code 的內容；<see cref="Id"/> 寫進 <see cref="McpGrant.AuthorizationCodeId"/> 防止重複兌換。</summary>
internal sealed record AuthorizationCode(Guid Id, string Subject, string? Email, string ClientId, string RedirectUri,
    string CodeChallenge, string Scope, string Resource, DateTimeOffset ExpiresAt);

internal sealed record AccessToken(Guid GrantId, string Subject, string? Email, string Scope, string Audience, DateTimeOffset ExpiresAt);
