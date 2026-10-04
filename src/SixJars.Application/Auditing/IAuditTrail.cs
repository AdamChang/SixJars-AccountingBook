namespace SixJars.Application.Auditing;

/// <summary>由寫入用的 command handler 明確呼叫，留下稽核記錄（ADR 0006）。</summary>
public interface IAuditTrail
{
    /// <summary>
    /// 加入 DbContext，與業務資料在同一次 SaveChanges 寫入；不會自己存檔。操作者取自目前登入的使用者。
    /// <paramref name="before"/> 必須在修改實體之前取得。快照型別直接重用 API DTO。
    /// </summary>
    void Record<T>(Guid bookId, AuditAction action, string entityType, Guid entityId, T? before, T? after);

    /// <summary>
    /// 同 <see cref="Record{T}"/>，但操作者由呼叫端指定。只給「還沒有登入身分」的寫入使用：
    /// 第一次登入時綁定 Google sub，OIDC callback 當下 cookie 尚未發出，操作者就是被綁定的 sub。
    /// </summary>
    void RecordAs<T>(string actorSubject, Guid bookId, AuditAction action, string entityType, Guid entityId, T? before, T? after);
}
