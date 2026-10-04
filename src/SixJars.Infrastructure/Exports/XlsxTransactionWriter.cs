using ClosedXML.Excel;
using SixJars.Application.Exports;

namespace SixJars.Infrastructure.Exports;

/// <summary>
/// xlsx（ClosedXML，MIT）：工作表「交易」，在記憶體中產生。日期與金額寫成真正的日期、數字，Excel 才能直接篩選、加總。
/// </summary>
/// <remarks>
/// 文字一律以 <see cref="IXLCell.SetValue(XLCellValue)"/> 寫成文字，<b>不使用 FormulaA1</b>，內容不會被當成公式。
/// 以公式字元開頭的文字另外設定 quote prefix（等同 Excel 手動輸入時前面加 <c>'</c>）：使用者之後在 Excel 編輯該格時，
/// 也不會被重新解讀成公式；儲存格內容維持原樣，不加任何字元。
/// </remarks>
internal sealed class XlsxTransactionWriter : ITransactionSheetWriter
{
    public const string SheetName = "交易";

    public TransactionExportFormat Format => TransactionExportFormat.Xlsx;
    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string Extension => "xlsx";

    public byte[] Write(IReadOnlyList<TransactionExportRow> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(SheetName);
        for (var c = 0; c < TransactionExportRow.Headers.Count; c++)
        {
            Text(sheet.Cell(1, c + 1), TransactionExportRow.Headers[c]);
        }

        sheet.Row(1).Style.Font.Bold = true;
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var row = i + 2;
            sheet.Cell(row, 1).SetValue(r.Date.ToDateTime(TimeOnly.MinValue)).Style.NumberFormat.Format = "yyyy-mm-dd";
            sheet.Cell(row, 2).SetValue(r.BudgetMonth);
            Text(sheet.Cell(row, 3), r.Kind);
            Text(sheet.Cell(row, 4), r.Account);
            Text(sheet.Cell(row, 5), r.CounterAccount);
            Text(sheet.Cell(row, 6), r.MainCategory);
            Text(sheet.Cell(row, 7), r.SubCategory);
            Text(sheet.Cell(row, 8), r.PlanningFund);
            sheet.Cell(row, 9).SetValue(r.Amount);
            Number(sheet.Cell(row, 10), r.LoanPrincipal);
            Number(sheet.Cell(row, 11), r.LoanInterest);
            Text(sheet.Cell(row, 12), r.Note);
        }

        sheet.SheetView.FreezeRows(1);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>null 留空白儲存格；否則寫成文字，以公式字元開頭的再加上 quote prefix。</summary>
    private static void Text(IXLCell cell, string? value)
    {
        if (value is null)
        {
            return;
        }

        cell.SetValue(value);
        if (SpreadsheetText.IsFormulaLike(value))
        {
            cell.Style.IncludeQuotePrefix = true;
        }
    }

    private static void Number(IXLCell cell, decimal? value)
    {
        if (value is { } number)
        {
            cell.SetValue(number);
        }
    }
}
