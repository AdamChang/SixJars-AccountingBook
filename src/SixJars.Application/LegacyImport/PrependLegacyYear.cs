using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Auditing;
using SixJars.Application.Books;
using SixJars.Application.Common;
using SixJars.Domain.Books;

namespace SixJars.Application.LegacyImport;

/// <summary>
/// 補匯舊年度：把帳本開始前一年的舊 Excel 接到既有帳本最前面（CLI <c>import-legacy --into</c>）。
/// CLI 直接連 DB、沒有登入身分，所以是 internal 的 <see cref="ICliOnlyRequest"/>、不實作 IBookScoped（spec D1）。
/// </summary>
/// <param name="FileName">來源檔案；稽核記錄只留檔名。</param>
/// <param name="DryRun">只做對應與檢查，回傳報告與銜接摘要，不寫入。</param>
internal sealed record PrependLegacyYear(LegacyWorkbook Workbook, string FileName, Guid BookId, bool DryRun)
    : IRequest<PrependLegacyYearResult>, ICliOnlyRequest;

/// <param name="Written">有寫入資料庫；報告有錯誤或 dry run 時為 false。</param>
/// <param name="NewOpeningDate">舊年度推算的期初日；年度檢查不通過時等於 <paramref name="PreviousOpeningDate"/>。</param>
internal sealed record PrependLegacyYearResult(
    bool Written, ImportReport Report, DateOnly PreviousOpeningDate, DateOnly NewOpeningDate,
    IReadOnlyList<JunctionLine> Junction, int Transactions, int PlannedExpenses,
    int ArchivedAccounts, int ArchivedPlanningFunds, int ArchivedCategories);

internal sealed class PrependLegacyYearValidator : AbstractValidator<PrependLegacyYear>
{
    public PrependLegacyYearValidator()
    {
        RuleFor(c => c.FileName).NotEmpty();
        RuleFor(c => c.BookId).NotEmpty();
    }
}

internal sealed class PrependLegacyYearHandler(ISixJarsDbContext db, IAuditTrail audit, TimeProvider clock)
    : IRequestHandler<PrependLegacyYear, PrependLegacyYearResult>
{
    public async Task<PrependLegacyYearResult> Handle(PrependLegacyYear request, CancellationToken cancellationToken)
    {
        // 1. 以 tracked 實體載入（D2）：並行衝突靠 Books 的 xmin，PrependHistory 一定會改期初日，所以一定會被檢查。
        var book = await db.GetBookForUpdateAsync(request.BookId, cancellationToken);
        var previousOpeningDate = book.OpeningDate;

        // 2. 年度不相連（spec M1）：不必往下對應。開始年度看最早的歸屬月份（舊 Excel 依月表分年）。
        var earliestBudgetMonth = await db.Transactions
            .Where(t => t.BookId == book.Id)
            .OrderBy(t => t.BudgetMonth)
            .Select(t => t.BudgetMonth)
            .Take(1)
            .ToListAsync(cancellationToken);
        var bookStartYear = LegacyJunction.BookStartYear(previousOpeningDate, earliestBudgetMonth.Count > 0 ? earliestBudgetMonth[0] : null);
        if (LegacyJunction.CheckYear(bookStartYear, request.Workbook.Settings.Year) is { } yearError)
        {
            var report = new ImportReport();
            report.AddError(LegacyJunction.Sheet, 0, yearError);
            return new PrependLegacyYearResult(false, report, previousOpeningDate, previousOpeningDate, [], 0, 0, 0, 0, 0);
        }

        // 3. 對應與檢查。對應會把只在舊年度出現的項目加入 tracked 帳本；不寫入時這些修改隨 DbContext 丟棄。
        var mapping = LegacyWorkbookMapper.MapInto(request.Workbook, book);
        var junction = LegacyJunction.Check(book, previousOpeningDate, mapping, mapping.Report);

        // 4. 補的是歷史資料，不是修改已鎖的帳：略過鎖帳檢查、鎖帳日不動，只提醒（R3-Q2）。
        if (book.LockDate is { } lockDate)
        {
            mapping.Report.AddWarning(LegacyJunction.Sheet, 0, $"帳本鎖帳日 {lockDate:yyyy-MM-dd}，補匯的舊年度交易略過鎖帳檢查。");
        }

        var result = new PrependLegacyYearResult(
            false, mapping.Report, previousOpeningDate, mapping.OpeningDate, junction,
            mapping.Transactions.Count, mapping.PlannedExpenses.Count,
            mapping.AddedAccounts.Count, mapping.AddedPlanningFunds.Count, mapping.AddedCategories.Count);

        // 5. 有錯誤（整批不寫，R2-Q2）或 dry run：到此為止。
        if (mapping.Report.Errors.Count > 0 || request.DryRun)
        {
            return result;
        }

        // 6. 單一 DB transaction 寫入（D8）。
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var before = OpeningsSnapshot.Of(book);
        book.PrependHistory(mapping.OpeningDate, mapping.AccountOpenings, mapping.FundOpenings);
        db.Transactions.AddRange(mapping.Transactions);
        db.PlannedExpenses.AddRange(mapping.PlannedExpenses);
        ArchiveAdded(book, mapping);

        audit.Record(book.Id.Value, AuditAction.Import, AuditEntityTypes.Book, book.Id.Value, null, new PrependImportSnapshot(
            ImportLegacyBookHandler.FileNameOnly(request.FileName), mapping.Transactions.Count, mapping.PlannedExpenses.Count,
            mapping.Report.Warnings.Count, previousOpeningDate));
        var (changedBefore, changedAfter) = before.ChangedAgainst(OpeningsSnapshot.Of(book));
        audit.Record(book.Id.Value, AuditAction.Update, AuditEntityTypes.Book, book.Id.Value, changedBefore, changedAfter);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result with { Written = true };
    }

    /// <summary>
    /// 只在舊年度出現的項目在交易全部產生後才封存（D7）。銜接檢查已保證它們在銜接點的餘額是 0，
    /// 這裡仍以實際算出的餘額呼叫，讓 Domain 的餘額檢查再守一次。
    /// </summary>
    private void ArchiveAdded(Book book, LegacyPrependMapping mapping)
    {
        var at = clock.GetUtcNow();
        foreach (var id in mapping.AddedAccounts)
        {
            var movement = mapping.Transactions.SelectMany(t => t.Postings).Where(p => p.AccountId == id).Sum(p => p.Amount);
            book.ArchiveAccount(id, book.GetAccount(id).OpeningBalance + movement, at);
        }

        foreach (var id in mapping.AddedPlanningFunds)
        {
            var movement = mapping.Transactions.Where(t => t.PlanningFundId == id).Sum(t => t.FundDelta);
            book.ArchivePlanningFund(id, book.GetPlanningFund(id).OpeningBalance + movement, at);
        }

        foreach (var id in mapping.AddedCategories)
        {
            book.ArchiveCategory(id, at);
        }
    }

    /// <summary>Import 稽核記錄的 After：比照 <c>ImportLegacyBook</c>，多一個補匯前的期初日（R3-Q4）。</summary>
    private sealed record PrependImportSnapshot(string FileName, int Transactions, int PlannedExpenses, int Warnings, DateOnly PreviousOpeningDate);

    private sealed record OpeningItem(Guid Id, string Name, decimal OpeningBalance);

    /// <summary>Update 稽核記錄的 Before／After：期初日與期初餘額（R3-Q4）。</summary>
    private sealed record OpeningsSnapshot(DateOnly OpeningDate, IReadOnlyList<OpeningItem> Accounts, IReadOnlyList<OpeningItem> PlanningFunds)
    {
        public static OpeningsSnapshot Of(Book book) => new(
            book.OpeningDate,
            [.. book.Accounts.Select(a => new OpeningItem(a.Id.Value, a.Name, a.OpeningBalance))],
            [.. book.PlanningFunds.Select(f => new OpeningItem(f.Id.Value, f.Name, f.OpeningBalance))]);

        /// <summary>
        /// 只留有變動的項目。以改寫前的項目為準：對應時新增的項目改寫前後是同一個值，不會被列入。
        /// </summary>
        public (OpeningsSnapshot Before, OpeningsSnapshot After) ChangedAgainst(OpeningsSnapshot after)
        {
            var changedAccounts = Changed(Accounts, after.Accounts);
            var changedFunds = Changed(PlanningFunds, after.PlanningFunds);
            return (new(OpeningDate, [.. changedAccounts.Select(c => c.Before)], [.. changedFunds.Select(c => c.Before)]),
                new(after.OpeningDate, [.. changedAccounts.Select(c => c.After)], [.. changedFunds.Select(c => c.After)]));
        }

        private static List<(OpeningItem Before, OpeningItem After)> Changed(IReadOnlyList<OpeningItem> before, IReadOnlyList<OpeningItem> after) =>
            [.. before.Join(after, b => b.Id, a => a.Id, (b, a) => (Before: b, After: a)).Where(p => p.Before.OpeningBalance != p.After.OpeningBalance)];
    }
}
