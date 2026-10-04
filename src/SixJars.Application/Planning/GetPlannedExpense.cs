using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Common;
using SixJars.Domain.Common;

namespace SixJars.Application.Planning;

public sealed record GetPlannedExpense(Guid BookId, Guid PlannedExpenseId) : IRequest<PlannedExpenseDto>;

internal sealed class GetPlannedExpenseHandler(ISixJarsDbContext db) : IRequestHandler<GetPlannedExpense, PlannedExpenseDto>
{
    public async Task<PlannedExpenseDto> Handle(GetPlannedExpense request, CancellationToken cancellationToken)
    {
        // 帳本與預定支出 Id 一起當查詢條件，避免讀到別本帳的資料（同 GetTransaction）。
        var bookId = new BookId(request.BookId);
        var plannedExpenseId = new PlannedExpenseId(request.PlannedExpenseId);
        // 要追蹤變更，db.GetVersion 才讀得到版本。
        var planned = await db.PlannedExpenses
            .SingleOrDefaultAsync(p => p.BookId == bookId && p.Id == plannedExpenseId, cancellationToken)
            ?? throw new NotFoundException($"找不到預定支出 {request.PlannedExpenseId}。");

        return PlannedExpenseDto.From(planned, db.GetVersion(planned));
    }
}
