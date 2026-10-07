namespace SixJars.Application.Auditing;

/// <summary>
/// 一次寫入的不可變紀錄（ADR 0006）；<see cref="Before"/>／<see cref="After"/> 是完整的 JSON 快照（jsonb），交易連同分錄。
/// 放在 Application 而不是 Domain：它不是業務概念，而是寫入用的副產品。
/// </summary>
public sealed class AuditEntry
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required Guid BookId { get; init; }
    public required DateTimeOffset At { get; init; }

    /// <summary>操作者的 Google <c>sub</c>。</summary>
    public required string ActorSubject { get; init; }

    public required AuditAction Action { get; init; }

    /// <summary>實體類型，取值見 <see cref="AuditEntityTypes"/>。</summary>
    public required string EntityType { get; init; }

    public required Guid EntityId { get; init; }

    /// <summary>修改前的快照；新增時為 null。</summary>
    public string? Before { get; init; }

    /// <summary>修改後的快照；刪除時為 null。</summary>
    public string? After { get; init; }
}

/// <summary><see cref="AuditEntry.EntityType"/> 的取值。</summary>
public static class AuditEntityTypes
{
    /// <summary>帳本本身：鎖帳日，以及匯入（Import 的摘要記錄）；EntityId 即帳本 Id。</summary>
    public const string Book = "Book";
    public const string Account = "Account";
    public const string PlanningFund = "PlanningFund";
    public const string Category = "Category";
    public const string Transaction = "Transaction";
    public const string PlannedExpense = "PlannedExpense";
    public const string RecurringPlannedExpense = "RecurringPlannedExpense";
    /// <summary>帳本成員：CLI 加入成員（Create，操作者 <c>cli</c>）；第一次登入時綁定 Google sub（Update，操作者即被綁定的 sub）。</summary>
    public const string BookMember = "BookMember";
}
