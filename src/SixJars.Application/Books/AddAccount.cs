using FluentValidation;
using MediatR;
using SixJars.Application.Auditing;
using SixJars.Application.Common;
using SixJars.Domain.Books;

namespace SixJars.Application.Books;

/// <summary>新增帳戶，回傳新帳戶的 Id。名稱重複由 Domain 擋下（422）。</summary>
public sealed record AddAccount(Guid BookId, string Name, AccountType Type, decimal OpeningBalance, bool CountsAsAvailableCash = true)
    : IRequest<Guid>, IBookScoped;

internal sealed class AddAccountValidator : AbstractValidator<AddAccount>
{
    public AddAccountValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Type).IsInEnum();
    }
}

internal sealed class AddAccountHandler(ISixJarsDbContext db, IAuditTrail audit) : IRequestHandler<AddAccount, Guid>
{
    public async Task<Guid> Handle(AddAccount request, CancellationToken cancellationToken)
    {
        var book = await db.GetBookForUpdateAsync(request.BookId, cancellationToken);
        var account = book.AddAccount(request.Name, request.Type, request.OpeningBalance, request.CountsAsAvailableCash);
        audit.Record(request.BookId, AuditAction.Create, AuditEntityTypes.Account, account.Id.Value, null, AccountDto.From(account));
        await db.SaveChangesAsync(cancellationToken);
        return account.Id.Value;
    }
}
