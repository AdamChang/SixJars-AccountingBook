using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Transactions;

namespace SixJars.Domain.Planning;

/// <summary>
/// 預定支出：歸屬於某月的固定／貸款／特別支出預計項目。
/// 未付時只占用月可用餘額（先佔額度），付款後連結到實際交易，改以實際金額為準（晚扣款）。
/// </summary>
public sealed class PlannedExpense
{
    // 參數名稱必須與屬性名稱一致，EF Core 的建構子綁定依賴這一點。
    private PlannedExpense(BookId bookId, BudgetMonth budgetMonth, CategoryId categoryId, AccountId? accountId, decimal estimatedAmount, string? note)
    {
        Id = PlannedExpenseId.New();
        BookId = bookId;
        BudgetMonth = budgetMonth;
        CategoryId = categoryId;
        AccountId = accountId;
        EstimatedAmount = estimatedAmount;
        Note = note;
    }

    public PlannedExpenseId Id { get; private set; }
    public BookId BookId { get; private set; }
    public BudgetMonth BudgetMonth { get; private set; }
    public CategoryId CategoryId { get; private set; }
    /// <summary>預計的付款帳戶；可不指定。</summary>
    public AccountId? AccountId { get; private set; }
    /// <summary>預估金額，沿用支出的符號慣例（負數）。</summary>
    public decimal EstimatedAmount { get; private set; }
    public string? Note { get; private set; }
    public TransactionId? PaidTransactionId { get; private set; }
    public bool IsPaid => PaidTransactionId is not null;
    /// <summary>由哪個週期項目產生；手動新增的為 null。<c>(SourceId, BudgetMonth)</c> 唯一，含已刪除（ADR 0009）。</summary>
    public RecurringPlannedExpenseId? SourceId { get; private set; }
    /// <summary>軟刪除的時間（ADR 0006）；null 表示未刪除。已刪除的預定支出不再占用月可用餘額。</summary>
    public DateTimeOffset? DeletedAt { get; private set; }
    public bool IsDeleted => DeletedAt is not null;

    /// <param name="id">只有還原備份（T42）時指定，保留原本的 Id；一般新增時省略，自動產生。驗證規則完全相同。</param>
    /// <param name="sourceId">由週期項目產生時的來源；產生由 <see cref="RecurringPlanner"/> 負責，這裡只為了還原備份與測試開放。</param>
    public static PlannedExpense Create(
        Book book, BudgetMonth budgetMonth, CategoryId categoryId, AccountId? accountId, decimal estimatedAmount, string? note = null,
        PlannedExpenseId? id = null, RecurringPlannedExpenseId? sourceId = null)
    {
        Validate(book, categoryId, accountId);
        var planned = new PlannedExpense(book.Id, budgetMonth, categoryId, accountId, estimatedAmount, note);
        if (id is { } fixedId)
        {
            planned.Id = fixedId;
        }

        planned.SourceId = sourceId;
        return planned;
    }

    /// <summary>修改未付的預定支出。付款後金額以實際交易為準，所以禁止修改。</summary>
    public void Update(Book book, BudgetMonth budgetMonth, CategoryId categoryId, AccountId? accountId, decimal estimatedAmount, string? note)
    {
        EnsureNotDeleted();

        if (IsPaid)
        {
            throw new DomainException($"預定支出 {Id.Value} 已付款，金額以實際交易為準，不可修改。");
        }

        if (book.Id != BookId)
        {
            throw new DomainException("帳本與預定支出不屬於同一本帳本。");
        }

        // (SourceId, BudgetMonth) 唯一：搬月份會讓原月份可以再產生一次，或撞上目標月份的同來源（P4 K plan D1）。
        if (SourceId is not null && budgetMonth != BudgetMonth)
        {
            throw new DomainException("由週期項目產生的預定支出不能改歸屬月份；請刪除後手動新增。");
        }

        Validate(book, categoryId, accountId);
        BudgetMonth = budgetMonth;
        CategoryId = categoryId;
        AccountId = accountId;
        EstimatedAmount = estimatedAmount;
        Note = note;
    }

    /// <summary>以來源週期項目的現值更新（ADR 0009「以現值更新」）；回傳內容是否有改變。只由 <see cref="RecurringPlanner"/> 呼叫。</summary>
    internal bool RefreshFrom(Book book, RecurringPlannedExpense source)
    {
        var changed = CategoryId != source.CategoryId || AccountId != source.AccountId
            || EstimatedAmount != source.DefaultAmount || Note != source.Note;
        if (changed)
        {
            Update(book, BudgetMonth, source.CategoryId, source.AccountId, source.DefaultAmount, source.Note);
        }

        return changed;
    }

    public void MarkPaid(Transaction transaction)
    {
        EnsureNotDeleted();

        if (IsPaid)
        {
            throw new DomainException($"預定支出 {Id.Value} 已付款，不可重複標記。");
        }

        if (transaction.BookId != BookId)
        {
            throw new DomainException("付款交易與預定支出不屬於同一本帳本。");
        }

        PaidTransactionId = transaction.Id;
    }

    /// <summary>
    /// 解除付款連結，回到未付（spec §9 O2）：付款交易被刪除時使用，月可用餘額改回以預估金額計算。
    /// 已刪除的預定支出不能修改，所以同樣擲出例外；刪除付款交易時 query filter 本來就查不到它，連結會原樣保留。
    /// </summary>
    public void MarkUnpaid()
    {
        EnsureNotDeleted();

        if (!IsPaid)
        {
            throw new DomainException($"預定支出 {Id.Value} 尚未付款，沒有可解除的付款連結。");
        }

        PaidTransactionId = null;
    }

    /// <summary>
    /// 軟刪除（spec §3.3）。已付款的也可以刪除：刪除的是「計畫」本身，已建立的付款交易不受影響，連結也保留。
    /// 已刪除的預定支出不能再刪除、修改或付款。
    /// </summary>
    public void Delete(DateTimeOffset at)
    {
        EnsureNotDeleted();
        DeletedAt = at;
    }

    private void EnsureNotDeleted()
    {
        if (IsDeleted)
        {
            throw new DomainException($"預定支出 {Id.Value} 已刪除，不可修改。");
        }
    }

    /// <summary>分類必須是固定、貸款或特別支出；有指定帳戶時，帳戶必須屬於這本帳本。</summary>
    private static void Validate(Book book, CategoryId categoryId, AccountId? accountId)
    {
        var category = book.GetCategory(categoryId);
        if (category.Kind != CategoryKind.Expense || category.Nature is not (ExpenseNature.Fixed or ExpenseNature.Loan or ExpenseNature.Special))
        {
            throw new DomainException($"預定支出只限固定、貸款、特別支出：「{category.Name}」。");
        }

        if (accountId is { } id)
        {
            book.GetAccount(id);
        }
    }
}
