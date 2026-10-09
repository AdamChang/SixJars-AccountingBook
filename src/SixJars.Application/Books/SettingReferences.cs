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

    /// <summary>主分類本身或其子分類，是否有任何週期預定支出。</summary>
    public static Task<bool> HasRecurringPlannedExpensesAsync(this ISixJarsDbContext db, Book book, CategoryId mainId, CancellationToken cancellationToken)
    {
        var ids = book.Categories.Where(c => c.Id == mainId || c.ParentId == mainId).Select(c => c.Id).ToList();
        return db.RecurringPlannedExpenses.AnyAsync(r => r.BookId == book.Id && ids.Contains(r.CategoryId), cancellationToken);
    }

    /// <summary>分類是否有預算。預算只會在浮動主分類上（CategoryBudget.Create），呼叫端只在這種分類時才需要查。</summary>
    public static Task<bool> HasBudgetAsync(this ISixJarsDbContext db, BookId bookId, CategoryId id, CancellationToken cancellationToken) =>
        db.CategoryBudgets.AnyAsync(b => b.BookId == bookId && b.CategoryId == id, cancellationToken);

    /// <summary>交易的帳戶、對方帳戶、分錄，以及預定支出與週期預定支出的帳戶。</summary>
    public static async Task<bool> IsAccountReferencedAsync(this ISixJarsDbContext db, BookId bookId, AccountId id, CancellationToken cancellationToken) =>
        await db.Transactions.IgnoreQueryFilters().AnyAsync(t => t.BookId == bookId
            && (t.AccountId == id || t.CounterAccountId == id || t.Postings.Any(p => p.AccountId == id)), cancellationToken)
        || await db.PlannedExpenses.IgnoreQueryFilters().AnyAsync(p => p.BookId == bookId && p.AccountId == id, cancellationToken)
        || await db.RecurringPlannedExpenses.AnyAsync(r => r.BookId == bookId && r.AccountId == id, cancellationToken);

    public static async Task<bool> IsCategoryReferencedAsync(this ISixJarsDbContext db, BookId bookId, CategoryId id, CancellationToken cancellationToken) =>
        await db.Transactions.IgnoreQueryFilters().AnyAsync(t => t.BookId == bookId && t.CategoryId == id, cancellationToken)
        || await db.PlannedExpenses.IgnoreQueryFilters().AnyAsync(p => p.BookId == bookId && p.CategoryId == id, cancellationToken)
        || await db.RecurringPlannedExpenses.AnyAsync(r => r.BookId == bookId && r.CategoryId == id, cancellationToken);

    public static Task<bool> IsPlanningFundReferencedAsync(this ISixJarsDbContext db, BookId bookId, PlanningFundId id, CancellationToken cancellationToken) =>
        db.Transactions.IgnoreQueryFilters().AnyAsync(t => t.BookId == bookId && t.PlanningFundId == id, cancellationToken);
}
