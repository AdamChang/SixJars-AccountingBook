using ExcelDataReader;
using SixJars.Application.LegacyImport;

namespace SixJars.Infrastructure.LegacyExcel;

/// <summary>一張工作表的儲存格值（0-based 列、欄），以 A1 位址存取。</summary>
internal sealed class SheetGrid(IReadOnlyList<object?[]> rows)
{
    public static IReadOnlyDictionary<string, SheetGrid> LoadAll(Stream stream)
    {
        var sheets = new Dictionary<string, SheetGrid>();
        using var reader = ExcelReaderFactory.CreateReader(stream);
        do
        {
            var rows = new List<object?[]>();
            while (reader.Read())
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                rows.Add(values);
            }

            sheets[reader.Name] = new SheetGrid(rows);
        }
        while (reader.NextResult());

        return sheets;
    }

    public object? Value(string address)
    {
        var (row, column) = Parse(address);
        return Value(row, column);
    }

    public string? Text(string address) => Value(address) switch
    {
        null => null,
        var value => value.ToString()!.Trim() is { Length: > 0 } text ? text : null,
    };

    public decimal Number(string address) => Value(address) switch
    {
        double d => Math.Round(Convert.ToDecimal(d), 4),
        int i => i,
        decimal m => Math.Round(m, 4),
        _ => 0m,
    };

    public bool HasNumber(string address) => Value(address) is double or int or decimal;

    public DateOnly? Date(string address) => Value(address) is DateTime dateTime ? DateOnly.FromDateTime(dateTime) : null;

    /// <summary>一欄中第 <paramref name="first"/>..<paramref name="last"/> 列的文字，排除空白與以 --- 開頭的分隔線。</summary>
    public IReadOnlyList<string> Texts(string column, int first, int last) =>
    [
        .. Enumerable.Range(first, last - first + 1)
            .Select(row => Text($"{column}{row}"))
            .OfType<string>()
            .Where(text => !text.StartsWith("---", StringComparison.Ordinal)),
    ];

    /// <summary>名稱欄與金額欄成對讀取；名稱為空的列跳過。</summary>
    public IReadOnlyList<LegacyNamedAmount> NamedAmounts(string nameColumn, string amountColumn, int first, int last) =>
    [
        .. Enumerable.Range(first, last - first + 1)
            .Where(row => Text($"{nameColumn}{row}") is not null)
            .Select(row => new LegacyNamedAmount(Text($"{nameColumn}{row}")!, Number($"{amountColumn}{row}"))),
    ];

    /// <summary>欄字母範圍，例如 Columns("AV", "AX") → AV、AW、AX。</summary>
    public static IEnumerable<string> Columns(string from, string to)
    {
        for (var index = ColumnIndex(from); index <= ColumnIndex(to); index++)
        {
            yield return ColumnName(index);
        }
    }

    private object? Value(int row, int column)
    {
        if (row >= rows.Count || column >= rows[row].Length)
        {
            return null;
        }

        return rows[row][column] is DBNull ? null : rows[row][column];
    }

    private static (int Row, int Column) Parse(string address)
    {
        var letters = new string([.. address.TakeWhile(char.IsLetter)]);
        var row = int.Parse(address.AsSpan(letters.Length));
        return (row - 1, ColumnIndex(letters));
    }

    private static int ColumnIndex(string letters) =>
        letters.ToUpperInvariant().Aggregate(0, (index, letter) => index * 26 + (letter - 'A' + 1)) - 1;

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            name = (char)('A' + (n - 1) % 26) + name;
        }

        return name;
    }
}
