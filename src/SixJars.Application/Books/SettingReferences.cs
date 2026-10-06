using Microsoft.EntityFrameworkCore;
using SixJars.Application.Common;
using SixJars.Domain.Books;
using SixJars.Domain.Common;

namespace SixJars.Application.Books;

/// <summary>
/// 設定項目是否被使用。已軟刪除的交易與預定支出也算（P4 J plan D3）：
/// RestoreBackup 會以 Domain 重建它們，設定被刪除或性質不符時，備份就無法還原。
/// </summary>
internal static class SettingReferences
{
    /// <summary>主分類本身或其子分類，是否有任何預定支出（含已刪除）。</summary>
    public static Task<bool> HasPlannedExpensesAsync(this ISixJarsDbContext db, Book book, CategoryId mainId, CancellationToken cancellationToken)
    {
        var ids = book.Categories.Where(c => c.Id == mainId || c.ParentId == mainId).Select(c => c.Id).ToList();
        return db.PlannedExpenses.IgnoreQueryFilters().AnyAsync(p => p.BookId == book.Id && ids.Contains(p.CategoryId), cancellationToken);
    }
}
