using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixJars.Application.Auditing;
using SixJars.Application.Common;
using SixJars.Domain.Common;
using SixJars.Domain.Members;

namespace SixJars.Application.LegacyImport;

/// <summary>
/// 把舊 Excel 記帳本匯入成一本新帳本（CLI <c>import-legacy</c>，spec §8.1）。
/// 搬家只做一次，不提供 API 上傳（spec §8.1 已否決），所以是 internal 的 <see cref="ICliOnlyRequest"/>。
/// </summary>
/// <param name="FileName">來源檔案；稽核記錄只留檔名，不留路徑（本機路徑可能含使用者名稱等個資）。</param>
/// <param name="DryRun">只做轉換與檢查（含同名帳本），回傳統計數字，不寫入。</param>
internal sealed record ImportLegacyBook(LegacyWorkbook Workbook, string FileName, string BookName, string OwnerEmail, bool DryRun)
    : IRequest<ImportLegacyBookResult>, ICliOnlyRequest;

/// <param name="BookId">新帳本的 Id；報告有錯誤或 dry run 時為 null（沒有寫入）。</param>
internal sealed record ImportLegacyBookResult(Guid? BookId, ImportReport Report, int Transactions, int PlannedExpenses);

internal sealed class ImportLegacyBookValidator : AbstractValidator<ImportLegacyBook>
{
    public ImportLegacyBookValidator()
    {
        RuleFor(c => c.FileName).NotEmpty();
        // 與 BookConfiguration 的名稱長度一致。
        RuleFor(c => c.BookName).NotEmpty().MaximumLength(100);
        RuleFor(c => c.OwnerEmail).NotEmpty().EmailAddress().MaximumLength(320);
    }
}

internal sealed class ImportLegacyBookHandler(ISixJarsDbContext db, IAuditTrail audit, TimeProvider clock)
    : IRequestHandler<ImportLegacyBook, ImportLegacyBookResult>
{
    public async Task<ImportLegacyBookResult> Handle(ImportLegacyBook request, CancellationToken cancellationToken)
    {
        var mapped = LegacyWorkbookMapper.Map(request.Workbook, request.BookName);
        var book = mapped.Book;
        var counts = (Transactions: mapped.Transactions.Count, PlannedExpenses: mapped.PlannedExpenses.Count);

        // 1. 報告有錯誤：有列沒被匯入，帳就對不起來；整本都不寫入，修好 Excel 再匯入。
        if (mapped.Report.Errors.Count > 0)
        {
            return new ImportLegacyBookResult(null, mapped.Report, counts.Transactions, counts.PlannedExpenses);
        }

        // 2. 同名帳本：拒絕，避免重複匯入。
        if (await db.Books.AnyAsync(b => b.Name == book.Name, cancellationToken))
        {
            throw new DomainException($"已有名稱為「{book.Name}」的帳本，不重複匯入。");
        }

        // 擁有者尚未綁定 Google sub：第一次以這個 email 登入時才綁定（T36）。dry run 也先建立，email 不合法時一併擋下。
        var owner = BookMember.Create(book.Id, request.OwnerEmail, BookRole.Owner, clock.GetUtcNow());

        // 3. dry run：到此為止，不寫入。
        if (request.DryRun)
        {
            return new ImportLegacyBookResult(null, mapped.Report, counts.Transactions, counts.PlannedExpenses);
        }

        // 4. 依相依順序分段寫入（帳本 → 帳務資料 → 成員與稽核記錄），整段包在同一個 DB transaction：
        //    任何一段失敗都整個回滾，不會留下只有帳本、沒有交易或沒有擁有者的半套資料。
        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        db.Books.Add(book);
        await db.SaveChangesAsync(cancellationToken);

        db.Transactions.AddRange(mapped.Transactions);
        db.PlannedExpenses.AddRange(mapped.PlannedExpenses);
        await db.SaveChangesAsync(cancellationToken);

        db.BookMembers.Add(owner);
        // 匯入只寫一筆摘要記錄（spec §7），不逐筆展開。
        audit.Record(book.Id.Value, AuditAction.Import, AuditEntityTypes.Book, book.Id.Value, null, new ImportSnapshot(
            FileNameOnly(request.FileName), counts.Transactions, counts.PlannedExpenses, mapped.Report.Warnings.Count));
        await db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new ImportLegacyBookResult(book.Id.Value, mapped.Report, counts.Transactions, counts.PlannedExpenses);
    }

    /// <summary>
    /// 只取檔名。兩種分隔字元都切：<see cref="Path.GetFileName(string)"/> 在 Linux 不認得 Windows 的 <c>\</c>，
    /// 而使用者在 Windows 本機執行 CLI。
    /// </summary>
    private static string FileNameOnly(string path) => path.Split('/', '\\')[^1];

    /// <summary>Import 稽核記錄的 After：<c>{ fileName, transactions, plannedExpenses, warnings }</c>。</summary>
    private sealed record ImportSnapshot(string FileName, int Transactions, int PlannedExpenses, int Warnings);
}
