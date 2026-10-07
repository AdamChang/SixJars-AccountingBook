// src/SixJars.Application/Planning/RefreshPlannedExpenses.cs
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Auditing;
using SixJars.Application.Books;
using SixJars.Application.Common;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;

namespace SixJars.Application.Planning;

/// <summary>以週期項目的現值更新某月未付、由週期項目產生的預定支出（ADR 0009）；只回報有改變的（P4 K plan D8）。</summary>
public sealed record RefreshPlannedExpenses(Guid BookId, int BudgetMonth) : IRequest<RefreshPlannedExpensesResult>, IBookScoped;

public sealed record RefreshPlannedExpensesResult(IReadOnlyList<PlannedExpenseDto> Updated, IReadOnlyList<RecurringSkipDto> Skipped);

internal sealed class RefreshPlannedExpensesValidator : AbstractValidator<RefreshPlannedExpenses>
{
    public RefreshPlannedExpensesValidator() =>
        RuleFor(c => c.BudgetMonth).Must(key => key % 100 is >= 1 and <= 12).WithMessage("歸屬月份必須是 yyyymm，月份介於 1 到 12。");
}

internal sealed class RefreshPlannedExpensesHandler(ISixJarsDbContext db, IAuditTrail audit)
    : IRequestHandler<RefreshPlannedExpenses, RefreshPlannedExpensesResult>
{
    public async Task<RefreshPlannedExpensesResult> Handle(RefreshPlannedExpenses request, CancellationToken cancellationToken)
    {
        var book = await db.GetBookAsNoTrackingAsync(request.BookId, cancellationToken);
        var month = BudgetMonth.FromKey(request.BudgetMonth);
        book.EnsureUnlocked(month);

        // 追蹤變更：存檔時以 xmin 做並行檢查。
        var candidates = await db.PlannedExpenses
            .Where(p => p.BookId == book.Id && p.BudgetMonth == month && p.SourceId != null && p.PaidTransactionId == null)
            .ToListAsync(cancellationToken);
        var sourceIds = candidates.Select(p => p.SourceId!.Value).Distinct().ToList();
        var items = await db.RecurringPlannedExpenses.AsNoTracking()
            .Where(r => r.BookId == book.Id && sourceIds.Contains(r.Id)).ToListAsync(cancellationToken);
        // before 快照必須在 Refresh 之前取得。
        var before = candidates.ToDictionary(p => p.Id, p => PlannedExpenseDto.From(p, db.GetVersion(p)));

        var result = RecurringPlanner.Refresh(book, month, items, candidates);
        foreach (var planned in result.Updated)
        {
            audit.Record(request.BookId, AuditAction.Update, AuditEntityTypes.PlannedExpense, planned.Id.Value,
                before[planned.Id], PlannedExpenseDto.From(planned, AuditSnapshots.UnknownVersion));
        }

        await db.SaveChangesAsync(cancellationToken);
        return new RefreshPlannedExpensesResult(
            [.. result.Updated.Select(p => PlannedExpenseDto.From(p, db.GetVersion(p)))],
            [.. result.Skipped.Select(RecurringSkipDto.From)]);
    }
}
