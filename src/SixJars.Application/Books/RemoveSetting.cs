using MediatR;
using SixJars.Application.Auditing;
using SixJars.Application.Common;
using SixJars.Domain.Common;

namespace SixJars.Application.Books;

/// <summary>刪除未被使用的設定項目；被使用時改用封存（spec §3.1）。</summary>
public sealed record RemoveSetting(Guid BookId, SettingKind Kind, Guid Id) : IRequest, IBookScoped;

internal sealed class RemoveSettingHandler(ISixJarsDbContext db, IAuditTrail audit) : IRequestHandler<RemoveSetting>
{
    public async Task Handle(RemoveSetting request, CancellationToken cancellationToken)
    {
        var book = await db.GetBookForUpdateAsync(request.BookId, cancellationToken);
        switch (request.Kind)
        {
            case SettingKind.Account:
                var accountId = new AccountId(request.Id);
                var account = AccountDto.From(book.GetAccount(accountId));
                book.RemoveAccount(accountId, await db.IsAccountReferencedAsync(book.Id, accountId, cancellationToken));
                audit.Record<AccountDto>(request.BookId, AuditAction.Delete, AuditEntityTypes.Account, request.Id, account, null);
                break;
            case SettingKind.PlanningFund:
                var fundId = new PlanningFundId(request.Id);
                var fund = PlanningFundDto.From(book.GetPlanningFund(fundId));
                book.RemovePlanningFund(fundId, await db.IsPlanningFundReferencedAsync(book.Id, fundId, cancellationToken));
                audit.Record<PlanningFundDto>(request.BookId, AuditAction.Delete, AuditEntityTypes.PlanningFund, request.Id, fund, null);
                break;
            default:
                var categoryId = new CategoryId(request.Id);
                var category = CategoryDto.From(book.GetCategory(categoryId));
                book.RemoveCategory(categoryId, await db.IsCategoryReferencedAsync(book.Id, categoryId, cancellationToken));
                audit.Record<CategoryDto>(request.BookId, AuditAction.Delete, AuditEntityTypes.Category, request.Id, category, null);
                break;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
