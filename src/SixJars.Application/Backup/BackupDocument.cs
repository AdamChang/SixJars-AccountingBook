using SixJars.Application.Auditing;
using SixJars.Application.Books;
using SixJars.Application.Planning;
using SixJars.Application.Transactions;
using SixJars.Domain.Members;

namespace SixJars.Application.Backup;

/// <summary>
/// 一本帳的完整備份（CONTEXT.md「備份」）：帳本設定、交易、預定支出、成員與稽核記錄，<b>包含已軟刪除的資料</b>。
/// 格式以 <see cref="FormatVersion"/> 標示；v2 起設定項目帶有 SortOrder 與 ArchivedAt（P4 J plan D8），
/// v3 起帶有週期預定支出與預定支出的來源（P4 K plan D3）；還原接受 v1–v3。
/// 內容直接重用 API 的 DTO；DTO 裡的 Version（xmin）只反映匯出當下的資料庫，還原後會不同，不具意義。
/// </summary>
public sealed record BackupDocument(
    int FormatVersion,
    DateTimeOffset ExportedAt,
    BookDto Book,
    IReadOnlyList<BackupTransaction> Transactions,
    IReadOnlyList<BackupPlannedExpense> PlannedExpenses,
    IReadOnlyList<BackupMember> Members,
    IReadOnlyList<AuditEntryDto> AuditEntries,
    IReadOnlyList<RecurringPlannedExpenseDto>? RecurringPlannedExpenses = null)
{
    public const int CurrentFormatVersion = 3;
}

/// <param name="DeletedAt">軟刪除的時間；null 表示未刪除。</param>
public sealed record BackupTransaction(TransactionDto Transaction, DateTimeOffset? DeletedAt);

/// <param name="DeletedAt">軟刪除的時間；null 表示未刪除。</param>
public sealed record BackupPlannedExpense(PlannedExpenseDto PlannedExpense, DateTimeOffset? DeletedAt);

/// <summary>帳本成員；<see cref="Id"/> 一併備份，稽核記錄（EntityType 為 BookMember）的 EntityId 才對得上。</summary>
/// <param name="GoogleSubject">已綁定的 Google sub；null 表示尚未登入過。</param>
public sealed record BackupMember(Guid Id, string Email, string? GoogleSubject, BookRole Role, DateTimeOffset AddedAt);
