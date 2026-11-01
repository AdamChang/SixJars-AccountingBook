namespace SixJars.Domain.Members;

/// <summary>
/// 一次 MCP 授權（ADR 0013）：使用者允許某個 AI client 以唯讀方式讀取自己可存取的帳本。
/// 和 <see cref="BookCreator"/> 一樣不屬於任何帳本，異動不留稽核記錄；撤銷只記時間、不刪列，事後可追查。
/// </summary>
/// <remarks>
/// Domain 只處理 refresh token 的 hash，不碰亂數與雜湊演算法（那是 Api 的 OAuth 細節）。
/// 保留上一個 hash 是為了分辨「已輪替掉的舊 token 又出現」（外洩，要撤銷）與「隨便猜的 token」（只拒絕）。
/// </remarks>
public sealed class McpGrant
{
    // 參數名稱必須與屬性名稱一致，EF Core 的建構子綁定依賴這一點。
    private McpGrant(string subject, string? email, string clientId, string scope, Guid authorizationCodeId,
        string refreshTokenHash, DateTimeOffset refreshTokenExpiresAt, DateTimeOffset createdAt)
    {
        Id = Guid.CreateVersion7();
        Subject = subject;
        Email = email;
        ClientId = clientId;
        Scope = scope;
        AuthorizationCodeId = authorizationCodeId;
        RefreshTokenHash = refreshTokenHash;
        RefreshTokenExpiresAt = refreshTokenExpiresAt;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    /// <summary>授權者的 Google sub；tool 以這個身分查詢，帳本權限仍由 BookAccessBehavior 判斷。</summary>
    public string Subject { get; private set; }
    /// <summary>授權當下的 email（正規化前的原值），只供 CLI 以 email 撤銷。</summary>
    public string? Email { get; private set; }
    /// <summary>client 的 CIMD URL。</summary>
    public string ClientId { get; private set; }
    public string Scope { get; private set; }
    /// <summary>兌換成這筆授權的 authorization code；unique，同一個 code 第二次兌換會撞到唯一性而被拒絕。</summary>
    public Guid AuthorizationCodeId { get; private set; }
    public string RefreshTokenHash { get; private set; }
    public string? PreviousRefreshTokenHash { get; private set; }
    public DateTimeOffset RefreshTokenExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastRefreshedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    public static McpGrant Issue(string subject, string? email, string clientId, string scope, Guid authorizationCodeId,
        string refreshTokenHash, DateTimeOffset now, TimeSpan refreshLifetime) =>
        new(subject, email, clientId, scope, authorizationCodeId, refreshTokenHash, now + refreshLifetime, now);

    /// <summary>
    /// 以 refresh token 換新 token：current 吻合就輪替；吻合的是上一個（已輪替掉的）就撤銷整筆授權（OAuth 2.1 對 public client 的要求）；
    /// 其餘（猜的、過期、已撤銷）只回 <see cref="McpRefreshOutcome.Invalid"/>，不改變狀態。
    /// </summary>
    public McpRefreshOutcome Refresh(string presentedHash, string newHash, DateTimeOffset now, TimeSpan refreshLifetime)
    {
        if (!IsActive)
        {
            return McpRefreshOutcome.Invalid;
        }

        if (presentedHash == PreviousRefreshTokenHash)
        {
            Revoke(now);
            return McpRefreshOutcome.Reused;
        }

        if (presentedHash != RefreshTokenHash || now >= RefreshTokenExpiresAt)
        {
            return McpRefreshOutcome.Invalid;
        }

        PreviousRefreshTokenHash = RefreshTokenHash;
        RefreshTokenHash = newHash;
        RefreshTokenExpiresAt = now + refreshLifetime;
        LastRefreshedAt = now;
        return McpRefreshOutcome.Rotated;
    }

    /// <summary>撤銷；已撤銷時保留第一次的時間。</summary>
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}

public enum McpRefreshOutcome
{
    Rotated,
    /// <summary>已輪替掉的舊 token 再次出現，授權已被撤銷。</summary>
    Reused,
    Invalid,
}
