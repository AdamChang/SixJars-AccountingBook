using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Common;
using SixJars.Domain.Common;

namespace SixJars.Application.Planning;

/// <summary>預定支出清單，依歸屬月份、再依建立順序排列。</summary>
/// <param name="BudgetMonth">歸屬月份（yyyymm）；null 表示全部月份。</param>
public sealed record ListPlannedExpenses(Guid BookId, int? BudgetMonth) : IRequest<IReadOnlyList<PlannedExpenseDto>>;

internal sealed class ListPlannedExpensesHandler(ISixJarsDbContext db) : IRequestHandler<ListPlannedExpenses, IReadOnlyList<PlannedExpenseDto>>
{
    public async Task<IReadOnlyList<PlannedExpenseDto>> Handle(ListPlannedExpenses request, CancellationToken cancellationToken)
    {
        var bookId = new BookId(request.BookId);
        // 要追蹤變更，db.GetVersion 才讀得到版本。
        var query = db.PlannedExpenses.Where(p => p.BookId == bookId);

        if (request.BudgetMonth is { } key)
        {
            var month = BudgetMonth.FromKey(key);
            query = query.Where(p => p.BudgetMonth == month);
        }

        // Guid.CreateVersion7 依建立時間遞增，同月份依 Id 排即為建立順序（同 ListTransactions）。
        var planned = await query.OrderBy(p => p.BudgetMonth).ThenBy(p => p.Id).ToListAsync(cancellationToken);
        return [.. planned.Select(p => PlannedExpenseDto.From(p, db.GetVersion(p)))];
    }
}
