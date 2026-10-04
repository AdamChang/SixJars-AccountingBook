namespace SixJars.Application.Common;

/// <summary>
/// 帳本範圍的 request：<see cref="BookAccessBehavior{TRequest, TResponse}"/> 會先確認登入者是該帳本的擁有者。
/// 所有帶 <c>Guid BookId</c> 的 request 都必須實作（由 BookScopeConventionTests 把關）。
/// </summary>
public interface IBookScoped
{
    Guid BookId { get; }
}
