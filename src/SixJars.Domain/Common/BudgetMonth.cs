namespace SixJars.Domain.Common;

/// <summary>歸屬月份：交易計入月可用餘額、預算與月報表的月份（可與交易日期不同）。</summary>
public readonly record struct BudgetMonth : IComparable<BudgetMonth>
{
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
    public static BudgetMonth FromKey(int key) => new(key / 100, key % 100);

    public int CompareTo(BudgetMonth other) => Key.CompareTo(other.Key);
    public static bool operator <(BudgetMonth left, BudgetMonth right) => left.CompareTo(right) < 0;
    public static bool operator >(BudgetMonth left, BudgetMonth right) => left.CompareTo(right) > 0;
    public static bool operator <=(BudgetMonth left, BudgetMonth right) => left.CompareTo(right) <= 0;
    public static bool operator >=(BudgetMonth left, BudgetMonth right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"{Year:D4}-{Month:D2}";
}
