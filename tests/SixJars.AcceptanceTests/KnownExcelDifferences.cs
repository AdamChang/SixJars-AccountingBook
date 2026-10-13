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
        new(3, "預算實際：金融交易", 27m,
            "同上一筆「手續費」−27：Excel「預算」工作表依主選單名稱加總而漏算；本系統計入浮動主分類「金融交易」的 actual（P4 L plan L9）。"),
        new(3, "月報浮動：金融交易", 27m,
            "同 3 月「手續費」−27：Excel 月工作表的浮動支出依主選單名稱 DSUM 而漏算；本系統計入浮動主分類「金融交易」（P4 M plan M7）。"),
        new(3, "月報浮動合計", 27m, "同上一筆，`X46` 是各浮動主分類的加總。"),
        new(0, "年報浮動：金融交易", 27m, "同上，總表的各月取自月工作表（月份 0 表示年度）。"),
        new(0, "年報浮動合計", 27m, "同上，`AU76`。"),
    ];

    public static decimal AdjustmentFor(int month, string label) =>
        All.Where(d => d.Month == month && d.Label == label).Sum(d => d.Adjustment);
}
