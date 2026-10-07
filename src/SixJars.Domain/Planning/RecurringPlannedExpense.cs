using SixJars.Domain.Books;
using SixJars.Domain.Common;

namespace SixJars.Domain.Planning;

public enum RecurrenceFrequency
{
    Monthly,
    Yearly,
}

/// <summary>
/// 週期預定支出（CONTEXT.md、spec §4.1）：每月，或每年的指定月份，在 [StartMonth, EndMonth] 之間適用。
/// 不會自動變成預定支出；使用者對某月明確「產生」時，由 <see cref="RecurringPlanner"/> 建立（ADR 0009）。
/// </summary>
public sealed class RecurringPlannedExpense
{
    private int[] _months = [];

    // EF Core 用；建構子參數對不到 _months，所以不用建構子綁定。
    private RecurringPlannedExpense()
    {
    }

    public RecurringPlannedExpenseId Id { get; private set; }
    public BookId BookId { get; private set; }
    /// <summary>只限固定或貸款性質（Q7）。</summary>
    public CategoryId CategoryId { get; private set; }
    public AccountId? AccountId { get; private set; }
    /// <summary>預設金額，沿用支出的符號慣例（負數）。</summary>
    public decimal DefaultAmount { get; private set; }
    public string? Note { get; private set; }
    public RecurrenceFrequency Frequency { get; private set; }
    /// <summary>每年的適用月份（1–12，已排序、去重）；每月時為空。</summary>
    public IReadOnlyList<int> Months => _months;
    public BudgetMonth StartMonth { get; private set; }
    /// <summary>最後一個適用的月份（含）；null 表示沒有結束。</summary>
    public BudgetMonth? EndMonth { get; private set; }

    /// <param name="id">只有還原備份時指定；一般新增時省略。</param>
    public static RecurringPlannedExpense Create(
        Book book, CategoryId categoryId, AccountId? accountId, decimal defaultAmount, string? note,
        RecurrenceFrequency frequency, IEnumerable<int> months, BudgetMonth startMonth, BudgetMonth? endMonth,
        RecurringPlannedExpenseId? id = null)
    {
        var item = new RecurringPlannedExpense { Id = id ?? RecurringPlannedExpenseId.New(), BookId = book.Id };
        item.Update(book, categoryId, accountId, defaultAmount, note, frequency, months, startMonth, endMonth);
        return item;
    }

    /// <summary>修改不回寫已產生的預定支出；要套用到某月請用「以現值更新」（ADR 0009）。</summary>
    public void Update(
        Book book, CategoryId categoryId, AccountId? accountId, decimal defaultAmount, string? note,
        RecurrenceFrequency frequency, IEnumerable<int> months, BudgetMonth startMonth, BudgetMonth? endMonth)
    {
        if (book.Id != BookId)
        {
            throw new DomainException("帳本與週期預定支出不屬於同一本帳本。");
        }

        // 先全部驗證完再寫入，失敗時不留下改了一半的狀態。
        var category = book.GetCategory(categoryId);
        if (category.Kind != CategoryKind.Expense || category.Nature is not (ExpenseNature.Fixed or ExpenseNature.Loan))
        {
            throw new DomainException($"週期預定支出只限固定、貸款支出：「{category.Name}」。");
        }

        if (accountId is { } account)
        {
            book.GetAccount(account);
        }

        var normalized = NormalizeMonths(frequency, months);
        if (endMonth is { } end && end < startMonth)
        {
            throw new DomainException("結束月份不能早於開始月份。");
        }

        CategoryId = categoryId;
        AccountId = accountId;
        DefaultAmount = defaultAmount;
        Note = note;
        Frequency = frequency;
        _months = normalized;
        StartMonth = startMonth;
        EndMonth = endMonth;
    }

    public bool IsDueIn(BudgetMonth month) =>
        month >= StartMonth
        && (EndMonth is null || month <= EndMonth.Value)
        && (Frequency == RecurrenceFrequency.Monthly || _months.Contains(month.Month));

    /// <param name="hasGenerated">由呼叫端查詢：是否有任何預定支出（含已刪除）以它為來源。</param>
    public void EnsureRemovable(bool hasGenerated)
    {
        if (hasGenerated)
        {
            throw new DomainException("這個週期項目已產生過預定支出，不能刪除；請改設結束月份。", DomainException.InUseCode);
        }
    }

    private static int[] NormalizeMonths(RecurrenceFrequency frequency, IEnumerable<int> months)
    {
        var normalized = months.Distinct().Order().ToArray();
        if (frequency == RecurrenceFrequency.Monthly)
        {
            return normalized.Length == 0 ? [] : throw new DomainException("每月的週期項目不指定月份。");
        }

        if (normalized.Length == 0 || normalized.Any(m => m is < 1 or > 12))
        {
            throw new DomainException("每年的週期項目至少要指定一個 1 到 12 的月份。");
        }

        return normalized;
    }
}
