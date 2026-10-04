using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Common;
using SixJars.Domain.Common;

namespace SixJars.Application.Transactions;

/// <summary>
/// 交易清單，依日期、再依建立順序排列。篩選條件皆可省略；P2 不分頁（個人帳本一年約 2,000 筆，前端依月份查詢）。
/// </summary>
/// <param name="From">交易日期下限（含）。</param>
/// <param name="To">交易日期上限（含）。</param>
/// <param name="BudgetMonth">歸屬月份（yyyymm），不是交易日期的月份。</param>
/// <param name="AccountId">只列出動到該帳戶的交易，包含該帳戶是對方帳戶的情形。</param>
public sealed record ListTransactions(Guid BookId, DateOnly? From, DateOnly? To, int? BudgetMonth, Guid? AccountId)
    : IRequest<IReadOnlyList<TransactionDto>>, IBookScoped;

/// <summary>歸屬月份必須是合法的 yyyymm；否則 <see cref="BudgetMonth.FromKey"/> 會擲例外，變成 500。</summary>
internal sealed class ListTransactionsValidator : AbstractValidator<ListTransactions>
{
    public ListTransactionsValidator() =>
        RuleFor(q => q.BudgetMonth)
            .Must(key => key!.Value % 100 is >= 1 and <= 12)
            .When(q => q.BudgetMonth is not null)
            .WithMessage("歸屬月份必須是 yyyymm，月份介於 1 到 12。");
}

internal sealed class ListTransactionsHandler(ISixJarsDbContext db) : IRequestHandler<ListTransactions, IReadOnlyList<TransactionDto>>
{
    public async Task<IReadOnlyList<TransactionDto>> Handle(ListTransactions request, CancellationToken cancellationToken)
    {
        var bookId = new BookId(request.BookId);
        // 要追蹤變更，db.GetVersion 才讀得到版本。
        var query = db.Transactions.Where(t => t.BookId == bookId);

        if (request.From is { } from)
        {
            query = query.Where(t => t.Date >= from);
        }

        if (request.To is { } to)
        {
            query = query.Where(t => t.Date <= to);
        }

        if (request.BudgetMonth is { } key)
        {
            var month = BudgetMonth.FromKey(key);
            query = query.Where(t => t.BudgetMonth == month);
        }

        if (request.AccountId is { } accountGuid)
        {
            // 分錄條件涵蓋一般情形；後兩個條件涵蓋沒有分錄的交易（例如同帳戶圈存的入新資金）。
            var accountId = new AccountId(accountGuid);
            query = query.Where(t => t.Postings.Any(p => p.AccountId == accountId)
                || t.AccountId == accountId
                || t.CounterAccountId == accountId);
        }

        // 同一天的交易依 Id 排：Guid.CreateVersion7 依建立時間遞增，PostgreSQL 的 uuid 以位元組比較，結果與建立順序一致。
        var transactions = await query.OrderBy(t => t.Date).ThenBy(t => t.Id).ToListAsync(cancellationToken);
        return [.. transactions.Select(t => TransactionDto.From(t, db.GetVersion(t)))];
    }
}
