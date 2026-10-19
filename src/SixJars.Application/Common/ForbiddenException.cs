namespace SixJars.Application.Common;

/// <summary>已登入但沒有權限執行這個操作（例如不在建立者名單，issue #7 Q18）；對應 403。帳本範圍的拒絕仍是 404（ADR 0005）。</summary>
public sealed class ForbiddenException(string message) : Exception(message);
