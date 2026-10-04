namespace SixJars.Application.Auditing;

/// <summary>稽核記錄的動作（spec §7）；資料庫存成名稱字串。</summary>
public enum AuditAction
{
    Create,
    Update,
    Delete,
    Import,
    Restore,
    LockDateChanged,
}
