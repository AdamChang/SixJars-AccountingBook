using MediatR;
using SixJars.Application.Auditing;
using SixJars.Application.Common;
using SixJars.Application.Ledger;
using SixJars.Domain.Common;

namespace SixJars.Application.Books;

/// <summary>封存設定項目；帳戶與財務規劃帳戶的餘額必須為 0（spec §3.1）。</summary>
public sealed record ArchiveSetting(Guid BookId, SettingKind Kind, Guid Id) : IRequest, IBookScoped;

public sealed record UnarchiveSetting(Guid BookId, SettingKind Kind, Guid Id) : IRequest, IBookScoped;

internal sealed class ArchiveSettingHandler(ISixJarsDbContext db, ILedgerSummaryQuery ledger, IAuditTrail audit, TimeProvider clock)
    : IRequestHandler<ArchiveSetting>
{
    // 包含未來日期的交易：封存表示之後不再使用，未來的交易也算在餘額內。
    private static readonly BalanceCutoff Everything = new BalanceCutoff.AsOf(DateOnly.MaxValue);

    public async Task Handle(ArchiveSetting request, CancellationToken cancellationToken)
    {
        var book = await db.GetBookForUpdateAsync(request.BookId, cancellationToken);
        var at = clock.GetUtcNow();
        switch (request.Kind)
        {
            case SettingKind.Account:
                var account = book.GetAccount(new AccountId(request.Id));
                var accountBefore = AccountDto.From(account);
                var postings = await ledger.PostingTotalsAsync(book.Id, Everything, cancellationToken);
                book.ArchiveAccount(account.Id, LedgerBalances.Account(account, postings), at);
                audit.Record(request.BookId, AuditAction.Update, AuditEntityTypes.Account, request.Id, accountBefore, AccountDto.From(account));
                break;
            case SettingKind.PlanningFund:
                var fund = book.GetPlanningFund(new PlanningFundId(request.Id));
                var fundBefore = PlanningFundDto.From(fund);
                var deltas = await ledger.FundDeltaTotalsAsync(book.Id, Everything, cancellationToken);
                book.ArchivePlanningFund(fund.Id, LedgerBalances.Fund(fund, deltas), at);
                audit.Record(request.BookId, AuditAction.Update, AuditEntityTypes.PlanningFund, request.Id, fundBefore, PlanningFundDto.From(fund));
                break;
            default:
                var category = book.GetCategory(new CategoryId(request.Id));
                var categoryBefore = CategoryDto.From(category);
                book.ArchiveCategory(category.Id, at);
                audit.Record(request.BookId, AuditAction.Update, AuditEntityTypes.Category, request.Id, categoryBefore, CategoryDto.From(category));
                break;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class UnarchiveSettingHandler(ISixJarsDbContext db, IAuditTrail audit) : IRequestHandler<UnarchiveSetting>
{
    public async Task Handle(UnarchiveSetting request, CancellationToken cancellationToken)
    {
        var book = await db.GetBookForUpdateAsync(request.BookId, cancellationToken);
        switch (request.Kind)
        {
            case SettingKind.Account:
                var account = book.GetAccount(new AccountId(request.Id));
                var accountBefore = AccountDto.From(account);
                book.UnarchiveAccount(account.Id);
                audit.Record(request.BookId, AuditAction.Update, AuditEntityTypes.Account, request.Id, accountBefore, AccountDto.From(account));
                break;
            case SettingKind.PlanningFund:
                var fund = book.GetPlanningFund(new PlanningFundId(request.Id));
                var fundBefore = PlanningFundDto.From(fund);
                book.UnarchivePlanningFund(fund.Id);
                audit.Record(request.BookId, AuditAction.Update, AuditEntityTypes.PlanningFund, request.Id, fundBefore, PlanningFundDto.From(fund));
                break;
            default:
                var category = book.GetCategory(new CategoryId(request.Id));
                var categoryBefore = CategoryDto.From(category);
                book.UnarchiveCategory(category.Id);
                audit.Record(request.BookId, AuditAction.Update, AuditEntityTypes.Category, request.Id, categoryBefore, CategoryDto.From(category));
                break;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
