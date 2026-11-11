namespace SixJars.Domain.Common;

/// <summary>歸屬月份：交易計入月可用餘額、預算與月報表的月份（可與交易日期不同）。</summary>
public readonly record struct BudgetMonth : IComparable<BudgetMonth>
{
    public const int MinYear = 1900;
    public const int MaxYear = 2999;

    public BudgetMonth(int year, int month)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 12);
        Year = year;
        Month = month;
    }

    public int Year { get; }
    public int Month { get; }

    /// <summary>yyyymm，供排序與持久化。</summary>
    public int Key => Year * 100 + Month;

    public static BudgetMonth Of(DateOnly date) => new(date.Year, date.Month);

    /// <summary>
    /// 讀取用：不檢查年份範圍，只給 <c>BudgetMonthConverter</c> 從 DB 還原既有資料，避免歷史資料讓讀取路徑擲例外。
    /// 寫入或接受外部輸入的路徑一律用 <see cref="Create"/>。
    /// </summary>
    public static BudgetMonth FromKey(int key) => new(key / 100, key % 100);

    /// <summary>寫入用：yyyymm 必須通過 <see cref="IsValidKey"/>，否則擲 <see cref="DomainException"/>（API 回 422）。</summary>
    public static BudgetMonth Create(int key) =>
        IsValidKey(key)
            ? FromKey(key)
            : throw new DomainException($"歸屬月份 {key} 不合法：必須是 yyyymm，年份介於 {MinYear} 到 {MaxYear}，月份介於 1 到 12。");

    /// <summary>yyyymm 是否為可支援的歸屬月份：年份在 <see cref="MinYear"/>–<see cref="MaxYear"/>，月份 1–12。</summary>
    public static bool IsValidKey(int key) =>
        key / 100 is >= MinYear and <= MaxYear && key % 100 is >= 1 and <= 12;

    public int CompareTo(BudgetMonth other) => Key.CompareTo(other.Key);
    public static bool operator <(BudgetMonth left, BudgetMonth right) => left.CompareTo(right) < 0;
    public static bool operator >(BudgetMonth left, BudgetMonth right) => left.CompareTo(right) > 0;
    public static bool operator <=(BudgetMonth left, BudgetMonth right) => left.CompareTo(right) <= 0;
    public static bool operator >=(BudgetMonth left, BudgetMonth right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"{Year:D4}-{Month:D2}";
}
