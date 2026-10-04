using FluentValidation;
using MediatR;
using SixJars.Application.Auditing;
using SixJars.Application.Common;

namespace SixJars.Application.Books;

/// <summary>新增財務規劃帳戶，回傳新帳戶的 Id。名稱重複由 Domain 擋下（422）。</summary>
public sealed record AddPlanningFund(Guid BookId, string Name, decimal OpeningBalance) : IRequest<Guid>;

internal sealed class AddPlanningFundValidator : AbstractValidator<AddPlanningFund>
{
    public AddPlanningFundValidator() => RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
}

internal sealed class AddPlanningFundHandler(ISixJarsDbContext db, IAuditTrail audit) : IRequestHandler<AddPlanningFund, Guid>
{
    public async Task<Guid> Handle(AddPlanningFund request, CancellationToken cancellationToken)
    {
        var book = await db.GetBookForUpdateAsync(request.BookId, cancellationToken);
        var fund = book.AddPlanningFund(request.Name, request.OpeningBalance);
        audit.Record(request.BookId, AuditAction.Create, AuditEntityTypes.PlanningFund, fund.Id.Value, null, PlanningFundDto.From(fund));
        await db.SaveChangesAsync(cancellationToken);
        return fund.Id.Value;
    }
}
