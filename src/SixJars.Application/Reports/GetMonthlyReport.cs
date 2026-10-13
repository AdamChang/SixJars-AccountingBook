using FluentValidation;
using MediatR;
using SixJars.Application.Books;
using SixJars.Application.Common;
using SixJars.Application.Ledger;
using SixJars.Application.Planning;
using SixJars.Domain.Common;
using SixJars.Domain.Reports;

namespace SixJars.Application.Reports;

/// <param name="BudgetMonth">歸屬月份（yyyymm）。</param>
public sealed record GetMonthlyReport(Guid BookId, int BudgetMonth) : IRequest<MonthlyReportDto>, IBookScoped;

public sealed record ReportLineDto(
    ReportLineKind Kind, Guid Id, string Name, decimal Amount, decimal? Principal, decimal? Interest, IReadOnlyList<ReportLineDto> Children)
{
    public static ReportLineDto From(MonthlyReportLine line) =>
        new(line.Kind, line.Id, line.Name, line.Amount, line.Principal, line.Interest, [.. line.Children.Select(From)]);
}

public sealed record ReportSectionDto(ReportSectionKind Kind, decimal Total, decimal? Unpaid, IReadOnlyList<ReportLineDto> Lines)
{
    public static ReportSectionDto From(MonthlyReportSection section) =>
        new(section.Kind, section.Total, section.Unpaid, [.. section.Lines.Select(ReportLineDto.From)]);
}

public sealed record PlanningLineDto(Guid FundId, string Name, decimal Allocation, decimal Withdrawal, decimal Return);

/// <param name="MonthlyDisposable">與 <c>/summary</c> 同一個方法算的月可用餘額（D3），供頁面最上方對照；報表合計刻意不等於它（ADR 0008）。</param>
public sealed record MonthlyReportDto(
    int BudgetMonth,
    decimal MonthlyDisposable,
    ReportSectionDto Income,
    IReadOnlyList<ReportSectionDto> Expenses,
    decimal ExpenseTotal,
    decimal UnpaidTotal,
    IReadOnlyList<PlanningLineDto> Planning)
{
    public static MonthlyReportDto From(MonthlyReportResult report, decimal monthlyDisposable) => new(
        report.Month.Key,
        monthlyDisposable,
        ReportSectionDto.From(report.Income),
        [.. report.Expenses.Select(ReportSectionDto.From)],
        report.ExpenseTotal,
        report.UnpaidTotal,
        [.. report.Planning.Select(p => new PlanningLineDto(p.FundId, p.Name, p.Allocation, p.Withdrawal, p.Return))]);
}

internal sealed class GetMonthlyReportValidator : AbstractValidator<GetMonthlyReport>
{
    public GetMonthlyReportValidator() =>
        RuleFor(q => q.BudgetMonth).Must(BudgetWrites.IsMonthKey).WithMessage(BudgetWrites.MonthKeyMessage);
}

/// <summary>資料庫往返 5 次（加上成員授權共 6 次，spec §6.2）：帳本、report flows、未付預定、月可用餘額 2 次（D3）。</summary>
internal sealed class GetMonthlyReportHandler(ISixJarsDbContext db, ILedgerSummaryQuery ledger)
    : IRequestHandler<GetMonthlyReport, MonthlyReportDto>
{
    public async Task<MonthlyReportDto> Handle(GetMonthlyReport request, CancellationToken cancellationToken)
    {
        var month = BudgetMonth.FromKey(request.BudgetMonth);
        var book = await db.GetBookAsNoTrackingAsync(request.BookId, cancellationToken);
        var flows = await ledger.ReportFlowsAsync(book.Id, month, month, cancellationToken);
        var unpaid = await ledger.UnpaidPlannedByCategoryAsync(book.Id, month, cancellationToken);
        var disposable = await ledger.MonthlyDisposableAsync(book, month, month, cancellationToken);
        return MonthlyReportDto.From(MonthlyReport.Build(book, month, flows, unpaid), disposable[month]);
    }
}
