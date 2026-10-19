using System.Globalization;
using FluentValidation;
using MediatR;
using SixJars.Application.Auditing;
using SixJars.Application.Common;
using SixJars.Domain.Books;

namespace SixJars.Application.Books;

/// <summary>新增標籤，回傳新標籤的 Id。名稱重複（不分大小寫）由 Domain 擋下（422）。</summary>
public sealed record AddTag(Guid BookId, string Name, TagColor Color) : IRequest<Guid>, IBookScoped;

internal sealed class AddTagValidator : AbstractValidator<AddTag>
{
    public AddTagValidator()
    {
        RuleFor(c => c.Name).TagName();
        RuleFor(c => c.Color).IsInEnum();
    }
}

internal sealed class AddTagHandler(ISixJarsDbContext db, IAuditTrail audit) : IRequestHandler<AddTag, Guid>
{
    public async Task<Guid> Handle(AddTag request, CancellationToken cancellationToken)
    {
        var book = await db.GetBookForUpdateAsync(request.BookId, cancellationToken);
        var tag = book.AddTag(request.Name, request.Color);
        audit.Record(request.BookId, AuditAction.Create, AuditEntityTypes.Tag, tag.Id.Value, null, TagDto.From(tag));
        await db.SaveChangesAsync(cancellationToken);
        return tag.Id.Value;
    }
}

internal static class TagNameRules
{
    /// <summary>名稱以 text element 計算長度（spec G5、D8），與 Domain 相同；不能用 MaximumLength（UTF-16 字元數）。</summary>
    public static IRuleBuilderOptions<T, string> TagName<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .Must(name => string.IsNullOrWhiteSpace(name) || new StringInfo(name.Trim()).LengthInTextElements <= Tag.MaxNameLength)
            .WithMessage($"標籤名稱最多 {Tag.MaxNameLength} 個字。");
}
