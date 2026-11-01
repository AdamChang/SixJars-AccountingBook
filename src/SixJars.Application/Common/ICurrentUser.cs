namespace SixJars.Application.Common;

/// <summary>目前登入的使用者（Google 的 sub 與 email）。</summary>
public interface ICurrentUser
{
    string Subject { get; }
    string? Email { get; }

    /// <summary>經由哪個 AI client（例如 mcp:claude.ai）；網頁與 CLI 為 null。</summary>
    string? Via => null;
}
