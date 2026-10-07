using SixJars.Domain.Books;
using SixJars.Domain.Common;

namespace SixJars.Domain.Planning;

public enum RecurringSkipReason
{
    AlreadyGenerated,
    CategoryArchived,
    AccountArchived,
    NotDue,
}

/// <param name="PlannedExpenseId">以現值更新時被略過的預定支出；產生時為 null。</param>
public sealed record RecurringSkip(RecurringPlannedExpenseId RecurringId, PlannedExpenseId? PlannedExpenseId, RecurringSkipReason Reason);

public sealed record GenerationResult(IReadOnlyList<PlannedExpense> Created, IReadOnlyList<RecurringSkip> Skipped);

public sealed record RefreshResult(IReadOnlyList<PlannedExpense> Updated, IReadOnlyList<RecurringSkip> Skipped);

/// <summary>週期項目與某月預定支出之間的規則（spec §4.2、ADR 0009）；不查詢資料庫，資料由呼叫端備妥。</summary>
public static class RecurringPlanner
{
    /// <param name="alreadyGenerated">該月已有預定支出（<b>含已刪除</b>）的來源。</param>
    public static GenerationResult Generate(
        Book book, BudgetMonth month, IEnumerable<RecurringPlannedExpense> items, IReadOnlySet<RecurringPlannedExpenseId> alreadyGenerated)
    {
        var created = new List<PlannedExpense>();
        var skipped = new List<RecurringSkip>();
        foreach (var item in items.Where(i => i.IsDueIn(month)))
        {
            // 已產生過的優先回報，即使之後分類被封存。
            var reason = alreadyGenerated.Contains(item.Id) ? RecurringSkipReason.AlreadyGenerated : UnusableReason(book, item);
            if (reason is { } r)
            {
                skipped.Add(new RecurringSkip(item.Id, null, r));
                continue;
            }

            created.Add(PlannedExpense.Create(book, month, item.CategoryId, item.AccountId, item.DefaultAmount, item.Note, sourceId: item.Id));
        }

        return new GenerationResult(created, skipped);
    }

    /// <param name="planned">該月的預定支出；只處理未付、未刪除、有來源的，其他的忽略。</param>
    public static RefreshResult Refresh(
        Book book, BudgetMonth month, IEnumerable<RecurringPlannedExpense> items, IEnumerable<PlannedExpense> planned)
    {
        var sources = items.ToDictionary(i => i.Id);
        var updated = new List<PlannedExpense>();
        var skipped = new List<RecurringSkip>();
        foreach (var expense in planned.Where(p => p.BudgetMonth == month && !p.IsPaid && !p.IsDeleted && p.SourceId is not null))
        {
            var source = sources[expense.SourceId!.Value];
            var reason = source.IsDueIn(month) ? UnusableReason(book, source) : RecurringSkipReason.NotDue;
            if (reason is { } r)
            {
                skipped.Add(new RecurringSkip(source.Id, expense.Id, r));
            }
            else if (expense.RefreshFrom(book, source))
            {
                updated.Add(expense);
            }
        }

        return new RefreshResult(updated, skipped);
    }

    private static RecurringSkipReason? UnusableReason(Book book, RecurringPlannedExpense item)
    {
        if (book.IsCategoryArchived(item.CategoryId))
        {
            return RecurringSkipReason.CategoryArchived;
        }

        return item.AccountId is { } accountId && book.GetAccount(accountId).IsArchived ? RecurringSkipReason.AccountArchived : null;
    }
}
