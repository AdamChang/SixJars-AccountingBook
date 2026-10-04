namespace SixJars.Application.Exports;

public enum TransactionExportFormat
{
    Csv,
    Xlsx,
}

/// <summary>把交易明細寫成檔案；每種格式一個實作，由 Infrastructure 提供（xlsx 需要 ClosedXML）。</summary>
public interface ITransactionSheetWriter
{
    TransactionExportFormat Format { get; }

    /// <summary>回應的 MIME 類型。</summary>
    string ContentType { get; }

    /// <summary>副檔名，不含句點。</summary>
    string Extension { get; }

    /// <summary>在記憶體中產生整個檔案；個人帳本一年約 2,000 筆，不需要串流。</summary>
    byte[] Write(IReadOnlyList<TransactionExportRow> rows);
}

/// <summary>
/// 試算表的文字欄位防護（CSV／formula injection，OWASP）：以 <c>= + - @</c>、tab 或 CR 開頭的文字，
/// Excel 開啟時會當成公式，例如備註 <c>=HYPERLINK("http://…")</c> 會變成可點的外部連結。
/// 只適用於文字欄位；金額等數字欄位一律寫成數字，負號不跳脫。
/// </summary>
public static class SpreadsheetText
{
    public static bool IsFormulaLike(string text) => text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r';
}
