using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Books;
using SixJars.Application.Common;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Transactions;

namespace SixJars.Application.Exports;

/// <summary>
/// 交易明細匯出（spec §8.3、CONTEXT.md「匯出」）：CSV 或 xlsx，<b>只含未刪除的交易</b>，不可還原（還原用備份）。
/// 依日期、再依建立順序排列，與交易清單相同。
/// </summary>
/// <param name="From">交易日期下限（含）；省略表示不限。</param>
/// <param name="To">交易日期上限（含）；省略表示不限。</param>
public sealed record ExportTransactions(Guid BookId, DateOnly? From, DateOnly? To, TransactionExportFormat Format)
    : IRequest<ExportedFile>, IBookScoped;

/// <summary>下載的檔案內容、MIME 類型與檔名。</summary>
public sealed record ExportedFile(byte[] Content, string ContentType, string FileName);

internal sealed class ExportTransactionsValidator : AbstractValidator<ExportTransactions>
{
    public ExportTransactionsValidator() =>
        RuleFor(q => q.From)
            .Must((q, from) => from <= q.To)
            .When(q => q.From is not null && q.To is not null)
            .WithMessage("起始日期不可晚於結束日期。");
}

internal sealed class ExportTransactionsHandler(ISixJarsDbContext db, IEnumerable<ITransactionSheetWriter> writers, TimeProvider clock)
    : IRequestHandler<ExportTransactions, ExportedFile>
{
    public async Task<ExportedFile> Handle(ExportTransactions request, CancellationToken cancellationToken)
    {
        var writer = writers.Single(w => w.Format == request.Format);
        var book = await db.GetBookAsNoTrackingAsync(request.BookId, cancellationToken);
        var bookId = new BookId(request.BookId);

        // 一般查詢：query filter 排除已軟刪除的交易（備份才用 IgnoreQueryFilters）。
        var query = db.Transactions.AsNoTracking().Where(t => t.BookId == bookId);
        if (request.From is { } from)
        {
            query = query.Where(t => t.Date >= from);
        }

        if (request.To is { } to)
        {
            query = query.Where(t => t.Date <= to);
        }

        var transactions = await query.OrderBy(t => t.Date).ThenBy(t => t.Id).ToListAsync(cancellationToken);
        var rows = transactions.Select(t => ToRow(book, t)).ToList();
        var today = TaipeiTime.DateOf(clock.GetUtcNow());
        return new ExportedFile(writer.Write(rows), writer.ContentType, $"sixjars-transactions-{today:yyyyMMdd}.{writer.Extension}");
    }

    private static TransactionExportRow ToRow(Book book, Transaction t)
    {
        var category = t.CategoryId is { } categoryId ? book.GetCategory(categoryId) : null;
        var parent = category?.ParentId is { } parentId ? book.GetCategory(parentId) : null;
        return new TransactionExportRow(
            t.Date,
            t.BudgetMonth.Key,
            TransactionKindNames.Of(t.Kind),
            book.GetAccount(t.AccountId).Name,
            t.CounterAccountId is { } counter ? book.GetAccount(counter).Name : null,
            parent?.Name ?? category?.Name,
            parent is null ? null : category!.Name,
            t.PlanningFundId is { } fund ? book.GetPlanningFund(fund).Name : null,
            t.Amount,
            t.LoanPrincipal,
            t.LoanInterest,
            t.Note);
    }
}
