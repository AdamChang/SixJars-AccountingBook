namespace SixJars.Application.Common;

/// <summary>使用者在台灣：「今天」與檔名上的日期一律以台北時間為準，不是伺服器（Cloud Run 為 UTC）的日期。</summary>
public static class TaipeiTime
{
    private static readonly TimeZoneInfo Taipei = TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");

    /// <summary><paramref name="instant"/> 在台北的日期；現在時間一律取自注入的 <see cref="TimeProvider"/>，測試才能固定。</summary>
    public static DateOnly DateOf(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Taipei).DateTime);

    /// <summary>台北 <paramref name="date"/> 當日 00:00 對應的 UTC 時間點：「今天」的稽核記錄計數用它當下限。</summary>
    public static DateTimeOffset StartOfDayUtc(DateOnly date) =>
        new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Taipei), TimeSpan.Zero);
}
