namespace SixJars.AcceptanceTests;

/// <summary>
/// 已確認是 Excel 本身算法造成、本系統刻意不跟隨的差異。比對時把 Excel 的期望值加上 <see cref="Adjustment"/>，
/// 金額必須完全相符；任一邊的數字變動都會讓比對重新失敗。
/// </summary>
internal sealed record KnownExcelDifference(int Month, string Label, decimal Adjustment, string Reason);

internal static class KnownExcelDifferences
{
    public static readonly IReadOnlyList<KnownExcelDifference> All =
    [
        new(3, "月可用餘額", -27m,
            "3 月流水帳第 20 列主選單「手續費」−27：Excel 的 J6 依主選單名稱 DSUM，「手續費」不屬於任何彙總列而漏算（銀行餘額有扣）；"
            + "本系統依 spec §5.2 計為支出「金融交易／手續費」，會扣月可用餘額。"),
    ];

    public static decimal AdjustmentFor(int month, string label) =>
        All.Where(d => d.Month == month && d.Label == label).Sum(d => d.Adjustment);
}
