using System.Globalization;
using System.Text;
using SixJars.Application.Exports;

namespace SixJars.Infrastructure.Exports;

/// <summary>
/// CSV（RFC 4180）：UTF-8 with BOM，Excel 開啟繁體中文才不會亂碼（spec §8.3）；每列以 CRLF 結尾。
/// 含逗號、雙引號、CR 或 LF 的欄位整欄以雙引號包住，雙引號寫兩次。日期 <c>yyyy-MM-dd</c>、數字一律 invariant culture。
/// </summary>
internal sealed class CsvTransactionWriter : ITransactionSheetWriter
{
    /// <summary>金額去掉 numeric 欄位補上的尾端 0（<c>-120.50</c> → <c>-120.5</c>），且不用科學記號。</summary>
    private const string NumberFormat = "0.############################";

    public TransactionExportFormat Format => TransactionExportFormat.Csv;
    public string ContentType => "text/csv";
    public string Extension => "csv";

    public byte[] Write(IReadOnlyList<TransactionExportRow> rows)
    {
        var csv = new StringBuilder();
        AppendLine(csv, TransactionExportRow.Headers.Select(Text));
        foreach (var r in rows)
        {
            AppendLine(csv,
            [
                r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                r.BudgetMonth.ToString(CultureInfo.InvariantCulture),
                Text(r.Kind),
                Text(r.Account),
                Text(r.CounterAccount),
                Text(r.MainCategory),
                Text(r.SubCategory),
                Text(r.PlanningFund),
                Number(r.Amount),
                Number(r.LoanPrincipal),
                Number(r.LoanInterest),
                Text(r.Note),
            ]);
        }

        // GetPreamble 即 BOM（EF BB BF）。
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    private static void AppendLine(StringBuilder csv, IEnumerable<string> fields) => csv.AppendJoin(',', fields).Append("\r\n");

    private static string Number(decimal? value) => value?.ToString(NumberFormat, CultureInfo.InvariantCulture) ?? "";

    /// <summary>文字欄位：先擋公式（前面加 <c>'</c>），再依 RFC 4180 加引號；順序不可對調，否則 <c>'</c> 會跑到引號外面。</summary>
    private static string Text(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        if (SpreadsheetText.IsFormulaLike(value))
        {
            value = "'" + value;
        }

        return value.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
    }
}
