using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Books;
using SixJars.Application.Common;
using SixJars.Application.Ledger;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;

namespace SixJars.Application.Planning;

/// <param name="BudgetMonth">歸屬月份（yyyymm）。</param>
public sealed record GetBudgets(Guid BookId, int BudgetMonth) : IRequest<BudgetSheetDto>, IBookScoped;

/// <param name="DefaultAmount">本月有覆寫值時也帶出，給行內編輯預設值用（P4 L plan D8）。</param>
public sealed record BudgetRowDto(Guid CategoryId, decimal? DefaultAmount, decimal? Budget, BudgetSource? Source, decimal Actual, decimal? Remaining);

public sealed record BudgetTotalsDto(decimal Budget, decimal Actual, decimal Remaining);

/// <summary>多包一層 totals，與 spec §5.2 的「每列一筆」不同（P4 L plan D6）：總覽卡直接顯示，規則不必在 TS 再寫一份。</summary>
public sealed record BudgetSheetDto(IReadOnlyList<BudgetRowDto> Rows, BudgetTotalsDto Totals)
{
    public static BudgetSheetDto From(BudgetSheetResult sheet) => new(
        [.. sheet.Rows.Select(r => new BudgetRowDto(r.CategoryId.Value, r.DefaultAmount, r.Budget, r.Source, r.Actual, r.Remaining))],
        new BudgetTotalsDto(sheet.Totals.Budget, sheet.Totals.Actual, sheet.Totals.Remaining));
}

internal sealed class GetBudgetsValidator : AbstractValidator<GetBudgets>
{
    public GetBudgetsValidator() =>
        RuleFor(q => q.BudgetMonth).Must(BudgetWrites.IsMonthKey).WithMessage(BudgetWrites.MonthKeyMessage);
}

/// <summary>資料庫往返 3 次（加上成員授權共 4 次，spec §5.2）：帳本、預算（覆寫值同一條 query JOIN）、支出彙總。</summary>
internal sealed class GetBudgetsHandler(ISixJarsDbContext db, ILedgerSummaryQuery ledger) : IRequestHandler<GetBudgets, BudgetSheetDto>
{
    public async Task<BudgetSheetDto> Handle(GetBudgets request, CancellationToken cancellationToken)
    {
        var bookId = new BookId(request.BookId);
        var month = BudgetMonth.FromKey(request.BudgetMonth);
        var book = await db.GetBookAsNoTrackingAsync(request.BookId, cancellationToken);
        var budgets = await db.CategoryBudgets.AsNoTracking().Where(b => b.BookId == bookId).ToListAsync(cancellationToken);
        var expenseTotals = await ledger.ExpenseTotalsByCategoryAsync(bookId, month, cancellationToken);
        return BudgetSheetDto.From(BudgetSheet.Build(book, month, budgets, expenseTotals));
    }
}
