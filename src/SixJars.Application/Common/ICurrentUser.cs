namespace SixJars.Application.Common;

/// <summary>目前登入的使用者（Google 的 sub 與 email）。</summary>
public interface ICurrentUser
{
    string Subject { get; }
    string? Email { get; }
}
