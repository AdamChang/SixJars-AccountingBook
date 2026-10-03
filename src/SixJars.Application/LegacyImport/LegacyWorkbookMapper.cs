using SixJars.Domain.Books;
using SixJars.Domain.Planning;
using SixJars.Domain.Transactions;

namespace SixJars.Application.LegacyImport;

public sealed record LegacyImportResult(Book Book, IReadOnlyList<Transaction> Transactions, IReadOnlyList<PlannedExpense> PlannedExpenses, ImportReport Report);

/// <summary>把舊記帳本的原始資料轉成 Domain 物件（spec §5）。純函式、無 I/O。</summary>
public static class LegacyWorkbookMapper
{
    public static LegacyImportResult Map(LegacyWorkbook workbook) => new MappingSession(workbook).Run();
}
