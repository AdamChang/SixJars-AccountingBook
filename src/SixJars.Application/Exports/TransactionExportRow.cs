namespace SixJars.Application.Exports;

/// <summary>
/// 交易明細匯出的一列（spec §8.3）：Id 都已換成名稱。分類是子分類時，主分類欄是它的 parent；本身是主分類時，子分類欄為 null。
/// </summary>
/// <param name="BudgetMonth">歸屬月份（yyyymm）。</param>
/// <param name="Kind">交易類型的中文名稱（<see cref="TransactionKindNames"/>）。</param>
/// <param name="LoanPrincipal">本金；只有貸款繳款有值。</param>
/// <param name="LoanInterest">利息 = 繳款總額 − 本金；只有貸款繳款有值。</param>
public sealed record TransactionExportRow(
    DateOnly Date,
    int BudgetMonth,
    string Kind,
    string Account,
    string? CounterAccount,
    string? MainCategory,
    string? SubCategory,
    string? PlanningFund,
    decimal Amount,
    decimal? LoanPrincipal,
    decimal? LoanInterest,
    string? Note)
{
    /// <summary>欄位標題，順序即檔案中的欄位順序；CSV 與 xlsx 共用。</summary>
    public static IReadOnlyList<string> Headers { get; } =
        ["日期", "歸屬月份", "交易類型", "帳戶", "對方帳戶", "主分類", "子分類", "財務規劃帳戶", "金額", "本金", "利息", "備註"];
}
