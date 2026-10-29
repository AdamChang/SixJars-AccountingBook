using System.ComponentModel;
using ModelContextProtocol.Server;

namespace SixJars.Api.Mcp;

/// <summary>
/// 信用卡對帳的指引（spec §7）：比對由 Claude 自己做（D3），這裡把使用者原本在 Excel 的比對方式寫成規則，
/// 同時放在 server instructions（每次連線都看得到）與 prompt（client 支援時可直接選用）。
/// </summary>
/// <remarks>不是 static class：SDK 以泛型 <c>WithPrompts&lt;T&gt;</c> 註冊，static class 不能當型別參數。</remarks>
[McpServerPromptType]
internal sealed class ReconcilePrompt
{
    public const string ServerInstructions = """
        這是「六罐子記帳本」的唯讀帳本資料。最常見的用途是信用卡帳單對帳：使用者提供信用卡 PDF 帳單，你讀出帳單明細後，
        用 list_books → list_accounts（找 type 為 CreditCard 的帳戶）→ list_account_transactions 取得記帳，逐筆比對。

        比對規則：
        - 以帳單的「消費日」對記帳的交易日期；記帳日期常比消費日晚 1 天，允許差 1 日。
        - 帳單金額是正的消費額；記帳的 amount 在卡上是負數（刷卡），退款與點數折抵是正數。比對時看絕對值與方向。
        - 一筆帳單可能對應多筆記帳（例如一次結帳記成兩個分類），一筆記帳也可能對應多筆帳單（例如高鐵來回拆成兩筆）。
        - 國外交易手續費通常沒有記帳，另外列出。
        - 繳卡費（kind 為 CardPayment）不是消費，不列入比對。
        - 帳單期間前後幾天的記帳可能屬於上一期或下一期，查詢期間要比帳單期間前後各多抓幾天，再判斷歸屬。

        輸出時依下列分組，每組列出明細與小計：
        A 日期與金額完全吻合；B 金額吻合、日期差 1 日；C 合併／拆分後吻合；D 帳單有、記帳沒有；
        E 國外交易手續費；F 記帳有、本期帳單沒有；G 其他疑點（金額不同、記帳缺金額等）。
        最後做差異分解：帳單本期消費總額、記帳淨額、差額，以及差額中「可解釋」（D、E、F）與「未解釋」的部分。
        你只能讀取，不能修改帳本；需要補記的項目請列出讓使用者自己處理。
        """;

    [McpServerPrompt(Name = "reconcile_credit_card_statement")]
    [Description("信用卡帳單對帳：讀取使用者附上的帳單，和記帳逐筆比對，依 A–G 分組列出差異。")]
    public static string Reconcile(
        [Description("信用卡帳戶名稱，例如「國泰Combo卡」；省略時請先列出信用卡帳戶讓使用者選")] string? account = null,
        [Description("帳單期間，例如「2026/8/14–9/15」；省略時以帳單上的期間為準")] string? period = null) =>
        $"""
        請比對我附上的信用卡帳單與六罐子記帳本的記帳。
        信用卡帳戶：{account ?? "（請先列出 CreditCard 帳戶讓我選）"}
        帳單期間：{period ?? "（以帳單上的期間為準）"}

        {ServerInstructions}
        """;
}
