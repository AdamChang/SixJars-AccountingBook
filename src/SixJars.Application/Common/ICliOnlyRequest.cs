namespace SixJars.Application.Common;

/// <summary>
/// 只給 CLI（管理工具）送出的 request：不經過帳本成員授權，因為 CLI 的操作者 <c>cli</c> 不是任何帳本的成員。
/// 這是 <see cref="IBookScoped"/> 慣例唯一的明確例外（由 BookScopeConventionTests 把關）。
/// 實作的 request 必須是 internal：Application 的 internal 只開放給 SixJars.Cli 與測試，SixJars.Api 因此無法參考。
/// </summary>
public interface ICliOnlyRequest;
