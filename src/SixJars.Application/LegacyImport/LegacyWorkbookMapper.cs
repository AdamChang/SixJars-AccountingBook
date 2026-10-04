using SixJars.Domain.Books;
using SixJars.Domain.Planning;
using SixJars.Domain.Transactions;

namespace SixJars.Application.LegacyImport;

public sealed record LegacyImportResult(Book Book, IReadOnlyList<Transaction> Transactions, IReadOnlyList<PlannedExpense> PlannedExpenses, ImportReport Report);

/// <summary>把舊記帳本的原始資料轉成 Domain 物件（spec §5）。純函式、無 I/O。</summary>
public static class LegacyWorkbookMapper
{
    /// <summary>未指定名稱時的帳本名稱（P1 的驗收與試算使用）。</summary>
    public const string DefaultBookName = "我的帳本";

    /// <param name="bookName">帳本名稱；正式匯入時由 CLI 的 <c>--book-name</c> 指定（T40）。</param>
    public static LegacyImportResult Map(LegacyWorkbook workbook, string bookName = DefaultBookName) =>
        new MappingSession(workbook, bookName).Run();
}
