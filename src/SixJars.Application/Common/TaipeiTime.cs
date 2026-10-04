namespace SixJars.Application.Common;

/// <summary>使用者在台灣：「今天」與檔名上的日期一律以台北時間為準，不是伺服器（Cloud Run 為 UTC）的日期。</summary>
public static class TaipeiTime
{
    private static readonly TimeZoneInfo Taipei = TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");

    /// <summary><paramref name="instant"/> 在台北的日期；現在時間一律取自注入的 <see cref="TimeProvider"/>，測試才能固定。</summary>
    public static DateOnly DateOf(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Taipei).DateTime);
}
