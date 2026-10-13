using FluentValidation;
using MediatR;
using SixJars.Application.Books;
using SixJars.Application.Common;
using SixJars.Application.Ledger;
using SixJars.Domain.Common;
using SixJars.Domain.Reports;

namespace SixJars.Application.Reports;

public sealed record GetYearlyReport(Guid BookId, int Year) : IRequest<YearlyReportDto>, IBookScoped;

public sealed record YearlyFiguresDto(IReadOnlyList<decimal> Months, decimal Total, decimal? ShareOfIncome, decimal? MonthlyAverage)
{
    public static YearlyFiguresDto From(YearlyFigures figures) => new(figures.Months, figures.Total, figures.ShareOfIncome, figures.MonthlyAverage);
}

public sealed record YearlyLineDto(ReportLineKind Kind, Guid Id, string Name, YearlyFiguresDto Figures, IReadOnlyList<YearlyLineDto> Children)
{
    public static YearlyLineDto From(YearlyReportLine line) =>
        new(line.Kind, line.Id, line.Name, YearlyFiguresDto.From(line.Figures), [.. line.Children.Select(From)]);
}

public sealed record YearlySectionDto(ReportSectionKind Kind, YearlyFiguresDto Figures, IReadOnlyList<YearlyLineDto> Lines);

public sealed record YearlyPlanningDto(IReadOnlyList<YearlyLineDto> Allocation, IReadOnlyList<YearlyLineDto> Withdrawal, IReadOnlyList<YearlyLineDto> Return);

/// <param name="AverageDivisor">月平均的分母；未來的年份與開帳之前為 null（spec Q10）。</param>
/// <param name="AverageFromMonth">月平均從幾月算起（開帳那一年是開帳月份）；沒有月平均時為 null。</param>
public sealed record YearlyReportDto(
    int Year,
    int? AverageDivisor,
    int? AverageFromMonth,
    YearlySectionDto Income,
    IReadOnlyList<YearlySectionDto> Expenses,
    YearlyFiguresDto ExpenseTotal,
    YearlyPlanningDto Planning)
{
    public static YearlyReportDto From(YearlyReportResult report) => new(
        report.Year,
        report.AverageDivisor,
        report.AverageFromMonth,
        Section(report.Income),
        [.. report.Expenses.Select(Section)],
        YearlyFiguresDto.From(report.ExpenseTotal),
        new YearlyPlanningDto(
            [.. report.Planning.Allocation.Select(YearlyLineDto.From)],
            [.. report.Planning.Withdrawal.Select(YearlyLineDto.From)],
            [.. report.Planning.Return.Select(YearlyLineDto.From)]));

    private static YearlySectionDto Section(YearlyReportSection section) =>
        new(section.Kind, YearlyFiguresDto.From(section.Figures), [.. section.Lines.Select(YearlyLineDto.From)]);
}

/// <summary>年份範圍與 issue #1 無關，只擋掉 yyyymm 會溢位或沒有意義的值（D11）。</summary>
internal sealed class GetYearlyReportValidator : AbstractValidator<GetYearlyReport>
{
    public GetYearlyReportValidator() =>
        RuleFor(q => q.Year).InclusiveBetween(1, 9999).WithMessage("年份必須介於 1 到 9999。");
}

/// <summary>資料庫往返 2 次（加上成員授權共 3 次，spec §6.2 上限 4）：帳本、report flows（D4）。</summary>
internal sealed class GetYearlyReportHandler(ISixJarsDbContext db, ILedgerSummaryQuery ledger, TimeProvider clock)
    : IRequestHandler<GetYearlyReport, YearlyReportDto>
{
    public async Task<YearlyReportDto> Handle(GetYearlyReport request, CancellationToken cancellationToken)
    {
        var book = await db.GetBookAsNoTrackingAsync(request.BookId, cancellationToken);
        var flows = await ledger.ReportFlowsAsync(
            book.Id, new BudgetMonth(request.Year, 1), new BudgetMonth(request.Year, 12), cancellationToken);
        // 月平均的分母依台北時間的「今天」（spec Q10），不是伺服器（Cloud Run 為 UTC）的日期
        var today = BudgetMonth.Of(TaipeiTime.DateOf(clock.GetUtcNow()));
        return YearlyReportDto.From(YearlyReport.Build(book, request.Year, flows, today));
    }
}
