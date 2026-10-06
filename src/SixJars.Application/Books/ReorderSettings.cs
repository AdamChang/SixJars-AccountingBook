using FluentValidation;
using MediatR;
using SixJars.Application.Auditing;
using SixJars.Application.Common;
using SixJars.Domain.Books;
using SixJars.Domain.Common;

namespace SixJars.Application.Books;

/// <summary>
/// 一次送出整組的新順序（含已封存的項目）。分類：<see cref="ParentId"/> 有值時排序該主分類的子分類，
/// 否則排序 <see cref="CategoryKind"/> 的主分類。
/// </summary>
public sealed record ReorderSettings(Guid BookId, SettingKind Kind, CategoryKind? CategoryKind, Guid? ParentId, IReadOnlyList<Guid> Ids)
    : IRequest, IBookScoped;

internal sealed class ReorderSettingsValidator : AbstractValidator<ReorderSettings>
{
    public ReorderSettingsValidator()
    {
        RuleFor(c => c.Ids).NotNull();
        // 錯誤欄位名稱對應 body 的 kind，前端才能標在正確的欄位上。
        RuleFor(c => c.CategoryKind).NotNull().IsInEnum()
            .When(c => c.Kind == SettingKind.Category && c.ParentId is null)
            .OverridePropertyName("Kind")
            .WithMessage("排序主分類時必須指定種類。");
    }
}

internal sealed class ReorderSettingsHandler(ISixJarsDbContext db, IAuditTrail audit) : IRequestHandler<ReorderSettings>
{
    public async Task Handle(ReorderSettings request, CancellationToken cancellationToken)
    {
        var book = await db.GetBookForUpdateAsync(request.BookId, cancellationToken);
        var before = BookDto.From(book);
        switch (request.Kind)
        {
            case SettingKind.Account:
                book.ReorderAccounts([.. request.Ids.Select(id => new AccountId(id))]);
                break;
            case SettingKind.PlanningFund:
                book.ReorderPlanningFunds([.. request.Ids.Select(id => new PlanningFundId(id))]);
                break;
            default:
                // 排序子分類時不使用種類，body 可以不帶；排序主分類時 validator 已確保有值。
                book.ReorderCategories(request.CategoryKind.GetValueOrDefault(),
                    request.ParentId is { } parentId ? new CategoryId(parentId) : null,
                    [.. request.Ids.Select(id => new CategoryId(id))]);
                break;
        }

        // 只記錄 SortOrder 有改變的項目，避免每次拖曳都寫出整組的記錄。
        var after = BookDto.From(book);
        RecordMoved(request.BookId, AuditEntityTypes.Account, before.Accounts, after.Accounts, a => a.Id);
        RecordMoved(request.BookId, AuditEntityTypes.PlanningFund, before.PlanningFunds, after.PlanningFunds, f => f.Id);
        RecordMoved(request.BookId, AuditEntityTypes.Category, before.Categories, after.Categories, c => c.Id);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>子 DTO 都是 record，值相等；SortOrder 不同即視為有移動。</summary>
    private void RecordMoved<T>(Guid bookId, string entityType, IReadOnlyList<T> before, IReadOnlyList<T> after, Func<T, Guid> idOf)
        where T : notnull
    {
        var beforeById = before.ToDictionary(idOf);
        foreach (var item in after.Where(item => !beforeById[idOf(item)].Equals(item)))
        {
            audit.Record(bookId, AuditAction.Update, entityType, idOf(item), beforeById[idOf(item)], item);
        }
    }
}
