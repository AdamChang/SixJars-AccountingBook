using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Common;
using SixJars.Domain.Common;

namespace SixJars.Application.Ledger;

/// <param name="BudgetMonth">歸屬月份（yyyymm）。</param>
/// <param name="AsOf">餘額的截止日（含）；null 表示 Asia/Taipei 的今天。</param>
public sealed record GetLedgerSummary(Guid BookId, int BudgetMonth, DateOnly? AsOf) : IRequest<LedgerSummaryDto>, IBookScoped;

/// <summary>歸屬月份必須是合法的 yyyymm；否則 <see cref="BudgetMonth.FromKey"/> 會擲例外，變成 500。</summary>
internal sealed class GetLedgerSummaryValidator : AbstractValidator<GetLedgerSummary>
{
    public GetLedgerSummaryValidator() =>
        RuleFor(q => q.BudgetMonth)
            .Must(key => key % 100 is >= 1 and <= 12)
            .WithMessage("歸屬月份必須是 yyyymm，月份介於 1 到 12。");
}

/// <summary>
/// 一次回傳摘要的所有數字（spec §5）。資料庫往返共 5 次：帳本設定、分錄加總、財務規劃帳戶加總、
/// 月可用餘額的交易與預定支出各 1 次；月可用餘額取年累計結果中的當月，不另外查詢。
/// </summary>
internal sealed class GetLedgerSummaryHandler(ISixJarsDbContext db, ILedgerSummaryQuery summary, TimeProvider clock)
    : IRequestHandler<GetLedgerSummary, LedgerSummaryDto>
{
    public async Task<LedgerSummaryDto> Handle(GetLedgerSummary request, CancellationToken cancellationToken)
    {
        var bookId = new BookId(request.BookId);
        var book = await db.Books.AsNoTracking().SingleOrDefaultAsync(b => b.Id == bookId, cancellationToken)
            ?? throw new NotFoundException($"找不到帳本 {request.BookId}。");
        var month = BudgetMonth.FromKey(request.BudgetMonth);
        // 使用者在台灣；「今天」以台北時間為準，不是伺服器（Cloud Run 為 UTC）的日期。
        var asOf = request.AsOf ?? TaipeiTime.DateOf(clock.GetUtcNow());

        var cutoff = new BalanceCutoff.AsOf(asOf);
        var postings = await summary.PostingTotalsAsync(bookId, cutoff, cancellationToken);
        var funds = await summary.FundDeltaTotalsAsync(bookId, cutoff, cancellationToken);
        var monthly = await summary.MonthlyDisposableAsync(book, new BudgetMonth(month.Year, 1), month, cancellationToken);

        return new LedgerSummaryDto(
            month.Key,
            asOf,
            monthly[month],
            monthly.Values.Sum(),
            LedgerBalances.AvailableCash(book, postings, includeEWallets: false),
            LedgerBalances.AvailableCash(book, postings, includeEWallets: true),
            [.. book.Accounts.Select(a => new BalanceDto(a.Id.Value, a.Name, LedgerBalances.Account(a, postings)))],
            [.. book.PlanningFunds.Select(f => new BalanceDto(f.Id.Value, f.Name, LedgerBalances.Fund(f, funds)))]);
    }
}
