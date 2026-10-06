namespace SixJars.Domain.Common;

/// <summary>違反領域規則時拋出。<see cref="Code"/> 會原樣放進 API 的 ProblemDetails，前端可據此判斷錯誤種類。</summary>
public sealed class DomainException(string message, string code = DomainException.RuleCode) : Exception(message)
{
    /// <summary>一般的領域規則違反。</summary>
    public const string RuleCode = "rule";

    /// <summary>異動落在鎖帳日（含）之前。</summary>
    public const string LockedCode = "locked";

    /// <summary>帳戶或財務規劃帳戶的餘額不為 0，不能封存。</summary>
    public const string NonZeroBalanceCode = "non-zero-balance";

    /// <summary>設定項目仍被使用（交易、預定支出、子分類……），不能刪除或變更。</summary>
    public const string InUseCode = "in-use";

    public string Code { get; } = code;
}
