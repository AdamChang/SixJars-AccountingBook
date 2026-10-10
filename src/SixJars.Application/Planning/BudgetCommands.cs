using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Auditing;
using SixJars.Application.Books;
using SixJars.Application.Common;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;

namespace SixJars.Application.Planning;

// 預算的寫入（spec §5.2）。與其他寫入 command 不同：不檢查鎖帳日（P4 L plan Q2）、不做樂觀並行（Q3）。

public sealed record SetDefaultBudget(Guid BookId, Guid CategoryId, decimal Amount) : IRequest<CategoryBudgetDto>, IBookScoped;

public sealed record RemoveDefaultBudget(Guid BookId, Guid CategoryId) : IRequest, IBookScoped;

/// <param name="BudgetMonth">歸屬月份（yyyymm）。</param>
public sealed record SetBudgetOverride(Guid BookId, Guid CategoryId, int BudgetMonth, decimal Amount) : IRequest<CategoryBudgetDto>, IBookScoped;

/// <param name="BudgetMonth">歸屬月份（yyyymm）。</param>
public sealed record RemoveBudgetOverride(Guid BookId, Guid CategoryId, int BudgetMonth) : IRequest, IBookScoped;

internal sealed class SetDefaultBudgetValidator : AbstractValidator<SetDefaultBudget>
{
    public SetDefaultBudgetValidator() => RuleFor(c => c.Amount).GreaterThanOrEqualTo(0m).WithMessage("預算金額不能是負數。");
}

internal sealed class SetBudgetOverrideValidator : AbstractValidator<SetBudgetOverride>
{
    public SetBudgetOverrideValidator()
    {
        RuleFor(c => c.BudgetMonth).Must(BudgetWrites.IsMonthKey).WithMessage(BudgetWrites.MonthKeyMessage);
        RuleFor(c => c.Amount).GreaterThanOrEqualTo(0m).WithMessage("預算金額不能是負數。");
    }
}

internal sealed class RemoveBudgetOverrideValidator : AbstractValidator<RemoveBudgetOverride>
{
    public RemoveBudgetOverrideValidator() =>
        RuleFor(c => c.BudgetMonth).Must(BudgetWrites.IsMonthKey).WithMessage(BudgetWrites.MonthKeyMessage);
}

internal sealed class SetDefaultBudgetHandler(ISixJarsDbContext db, IAuditTrail audit) : IRequestHandler<SetDefaultBudget, CategoryBudgetDto>
{
    public Task<CategoryBudgetDto> Handle(SetDefaultBudget request, CancellationToken cancellationToken) =>
        db.SetBudgetAsync(audit, request.BookId, request.CategoryId, b => b.SetDefault(request.Amount), cancellationToken);
}

internal sealed class RemoveDefaultBudgetHandler(ISixJarsDbContext db, IAuditTrail audit) : IRequestHandler<RemoveDefaultBudget>
{
    public Task Handle(RemoveDefaultBudget request, CancellationToken cancellationToken) =>
        db.RemoveBudgetValueAsync(audit, request.BookId, request.CategoryId, b => b.RemoveDefault(),
            $"分類 {request.CategoryId} 沒有預設預算。", cancellationToken);
}

internal sealed class SetBudgetOverrideHandler(ISixJarsDbContext db, IAuditTrail audit) : IRequestHandler<SetBudgetOverride, CategoryBudgetDto>
{
    public Task<CategoryBudgetDto> Handle(SetBudgetOverride request, CancellationToken cancellationToken) =>
        db.SetBudgetAsync(audit, request.BookId, request.CategoryId,
            b => b.SetOverride(BudgetMonth.FromKey(request.BudgetMonth), request.Amount), cancellationToken);
}

internal sealed class RemoveBudgetOverrideHandler(ISixJarsDbContext db, IAuditTrail audit) : IRequestHandler<RemoveBudgetOverride>
{
    public Task Handle(RemoveBudgetOverride request, CancellationToken cancellationToken) =>
        db.RemoveBudgetValueAsync(audit, request.BookId, request.CategoryId, b => b.RemoveOverride(BudgetMonth.FromKey(request.BudgetMonth)),
            $"分類 {request.CategoryId} 在 {request.BudgetMonth} 沒有預算覆寫值。", cancellationToken);
}

internal static class BudgetWrites
{
    public const string MonthKeyMessage = "歸屬月份必須是 yyyymm，月份介於 1 到 12。";

    /// <summary>同 GetLedgerSummaryValidator；否則 <see cref="BudgetMonth.FromKey"/> 會擲例外，變成 500（年份範圍是 issue #1，P4 L plan Q5）。</summary>
    public static bool IsMonthKey(int key) => key % 100 is >= 1 and <= 12;

    /// <summary>
    /// 設定一格：沒有預算時先建立（稽核 Create），其餘 Update；同值照樣寫稽核（P4 L plan Q8a、Q8b）。
    /// 只有建立時需要帳本（驗證分類），已有預算時不載入帳本。
    /// </summary>
    public static async Task<CategoryBudgetDto> SetBudgetAsync(
        this ISixJarsDbContext db, IAuditTrail audit, Guid bookId, Guid categoryId, Action<CategoryBudget> set, CancellationToken cancellationToken)
    {
        var budget = await db.FindCategoryBudgetAsync(bookId, categoryId, cancellationToken);
        var before = budget is null ? null : CategoryBudgetDto.From(budget);
        if (budget is null)
        {
            var book = await db.GetBookAsNoTrackingAsync(bookId, cancellationToken);
            budget = CategoryBudget.Create(book, new CategoryId(categoryId));
            db.CategoryBudgets.Add(budget);
        }

        set(budget);
        var after = CategoryBudgetDto.From(budget);
        audit.Record(bookId, before is null ? AuditAction.Create : AuditAction.Update, AuditEntityTypes.CategoryBudget, categoryId, before, after);
        await db.SaveChangesAsync(cancellationToken);
        return after;
    }

    /// <summary>清除一格：不存在是 404（Q8c）；清到全空就刪除整筆並記 Delete（Q1c、Q8a）。</summary>
    public static async Task RemoveBudgetValueAsync(
        this ISixJarsDbContext db, IAuditTrail audit, Guid bookId, Guid categoryId, Func<CategoryBudget, bool> remove, string notFoundMessage,
        CancellationToken cancellationToken)
    {
        var budget = await db.FindCategoryBudgetAsync(bookId, categoryId, cancellationToken) ?? throw new NotFoundException(notFoundMessage);
        var before = CategoryBudgetDto.From(budget);
        if (!remove(budget))
        {
            throw new NotFoundException(notFoundMessage);
        }

        CategoryBudgetDto? after = null;
        if (budget.IsEmpty)
        {
            db.CategoryBudgets.Remove(budget);
        }
        else
        {
            after = CategoryBudgetDto.From(budget);
        }

        audit.Record(bookId, after is null ? AuditAction.Delete : AuditAction.Update, AuditEntityTypes.CategoryBudget, categoryId, before, after);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>帳本與分類 Id 一起當查詢條件；沒有預算時回傳 null（設定時要建立，清除時才是 404）。</summary>
    private static async Task<CategoryBudget?> FindCategoryBudgetAsync(
        this ISixJarsDbContext db, Guid bookId, Guid categoryId, CancellationToken cancellationToken)
    {
        var book = new BookId(bookId);
        var category = new CategoryId(categoryId);
        return await db.CategoryBudgets.SingleOrDefaultAsync(b => b.BookId == book && b.CategoryId == category, cancellationToken);
    }
}
