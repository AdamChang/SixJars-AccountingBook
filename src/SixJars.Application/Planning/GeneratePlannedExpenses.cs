// src/SixJars.Application/Planning/GeneratePlannedExpenses.cs
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Auditing;
using SixJars.Application.Books;
using SixJars.Application.Common;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;

namespace SixJars.Application.Planning;

/// <summary>對某個歸屬月份明確產生週期項目的預定支出（ADR 0009）；冪等，已產生（含已刪除）的回報為略過。</summary>
public sealed record GeneratePlannedExpenses(Guid BookId, int BudgetMonth) : IRequest<GeneratePlannedExpensesResult>, IBookScoped;

/// <param name="PlannedExpenseId">以現值更新時被略過的預定支出；產生時為 null。</param>
public sealed record RecurringSkipDto(Guid RecurringId, Guid? PlannedExpenseId, RecurringSkipReason Reason)
{
    public static RecurringSkipDto From(RecurringSkip skip) => new(skip.RecurringId.Value, skip.PlannedExpenseId?.Value, skip.Reason);
}

public sealed record GeneratePlannedExpensesResult(IReadOnlyList<PlannedExpenseDto> Created, IReadOnlyList<RecurringSkipDto> Skipped);

internal sealed class GeneratePlannedExpensesValidator : AbstractValidator<GeneratePlannedExpenses>
{
    public GeneratePlannedExpensesValidator() =>
        RuleFor(c => c.BudgetMonth).Must(key => key % 100 is >= 1 and <= 12).WithMessage("歸屬月份必須是 yyyymm，月份介於 1 到 12。");
}

internal sealed class GeneratePlannedExpensesHandler(ISixJarsDbContext db, IAuditTrail audit)
    : IRequestHandler<GeneratePlannedExpenses, GeneratePlannedExpensesResult>
{
    public async Task<GeneratePlannedExpensesResult> Handle(GeneratePlannedExpenses request, CancellationToken cancellationToken)
    {
        var book = await db.GetBookAsNoTrackingAsync(request.BookId, cancellationToken);
        var month = BudgetMonth.FromKey(request.BudgetMonth);
        book.EnsureUnlocked(month);

        var items = await db.RecurringPlannedExpenses.AsNoTracking()
            .Where(r => r.BookId == book.Id).OrderBy(r => r.Id).ToListAsync(cancellationToken);
        // 含已刪除：刪除代表使用者決定這個月沒有這筆（ADR 0009）。
        var generated = await db.PlannedExpenses.IgnoreQueryFilters()
            .Where(p => p.BookId == book.Id && p.BudgetMonth == month && p.SourceId != null)
            .Select(p => p.SourceId!.Value)
            .ToListAsync(cancellationToken);

        var result = RecurringPlanner.Generate(book, month, items, generated.ToHashSet());
        foreach (var planned in result.Created)
        {
            db.PlannedExpenses.Add(planned);
            audit.Record(request.BookId, AuditAction.Create, AuditEntityTypes.PlannedExpense, planned.Id.Value,
                null, PlannedExpenseDto.From(planned, AuditSnapshots.UnknownVersion));
        }

        await db.SaveChangesAsync(cancellationToken);
        return new GeneratePlannedExpensesResult(
            [.. result.Created.Select(p => PlannedExpenseDto.From(p, db.GetVersion(p)))],
            [.. result.Skipped.Select(RecurringSkipDto.From)]);
    }
}
