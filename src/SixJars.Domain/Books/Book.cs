using SixJars.Domain.Common;

namespace SixJars.Domain.Books;

/// <summary>帳本：連續不分年度的記帳邊界（ADR 0002），也是帳戶、財務規劃帳戶與分類的聚合根。</summary>
public sealed class Book
{
    private readonly List<Account> _accounts = [];
    private readonly List<PlanningFund> _planningFunds = [];
    private readonly List<Category> _categories = [];

    public Book(string name, DateOnly openingDate)
    {
        Id = BookId.New();
        Name = RequireName(name);
        OpeningDate = openingDate;
    }

    public BookId Id { get; private set; }
    public string Name { get; private set; }
    /// <summary>期初餘額的基準日；所有帳戶的期初餘額都視為此日結束時的餘額。</summary>
    public DateOnly OpeningDate { get; private set; }

    /// <summary>此日（含）以前的交易不可再新增、修改或刪除（CONTEXT.md 鎖帳日）。可前移、後移或清除（spec §9 O3）。</summary>
    public DateOnly? LockDate { get; private set; }

    public IReadOnlyList<Account> Accounts => _accounts;
    public IReadOnlyList<PlanningFund> PlanningFunds => _planningFunds;
    public IReadOnlyList<Category> Categories => _categories;

    public Account AddAccount(string name, AccountType type, decimal openingBalance = 0m, bool countsAsAvailableCash = true)
    {
        name = RequireName(name);
        if (FindAccount(name) is not null)
        {
            throw new DomainException($"帳戶名稱「{name}」已存在。");
        }

        var account = new Account(AccountId.New(), name, type, openingBalance, type == AccountType.Cash && countsAsAvailableCash);
        _accounts.Add(account);
        return account;
    }

    public PlanningFund AddPlanningFund(string name, decimal openingBalance = 0m)
    {
        name = RequireName(name);
        if (FindPlanningFund(name) is not null)
        {
            throw new DomainException($"財務規劃帳戶名稱「{name}」已存在。");
        }

        var fund = new PlanningFund(PlanningFundId.New(), name, openingBalance);
        _planningFunds.Add(fund);
        return fund;
    }

    public Category AddIncomeCategory(string name) => AddMainCategory(name, CategoryKind.Income, nature: null);

    public Category AddExpenseCategory(string name, ExpenseNature nature) => AddMainCategory(name, CategoryKind.Expense, nature);

    public Category AddSubCategory(CategoryId parentId, string name)
    {
        var parent = GetCategory(parentId);
        if (!parent.IsMain)
        {
            throw new DomainException($"分類只有兩層，「{parent.Name}」不是主分類，不能再加子分類「{name}」。");
        }

        name = RequireName(name);
        if (_categories.Any(c => c.ParentId == parentId && c.Name == name))
        {
            throw new DomainException($"主分類「{parent.Name}」底下已有子分類「{name}」。");
        }

        var category = new Category(CategoryId.New(), name, parent.Kind, parent.Nature, parentId);
        _categories.Add(category);
        return category;
    }

    public void SetLockDate(DateOnly? lockDate) => LockDate = lockDate;

    /// <summary>交易日期落在鎖帳日（含）以前時擲出 <see cref="DomainException"/>，code 為 <see cref="DomainException.LockedCode"/>。</summary>
    public void EnsureUnlocked(DateOnly date)
    {
        if (LockDate is { } lockDate && date <= lockDate)
        {
            throw new DomainException($"{date:yyyy-MM-dd} 在鎖帳日 {lockDate:yyyy-MM-dd}（含）以前，不可異動。", DomainException.LockedCode);
        }
    }

    /// <summary>預定支出以歸屬月份的最後一天判斷（spec §3.1）。</summary>
    public void EnsureUnlocked(BudgetMonth month) =>
        EnsureUnlocked(new DateOnly(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month)));

    public Account GetAccount(AccountId id) =>
        _accounts.Find(a => a.Id == id) ?? throw new DomainException($"找不到帳戶 {id.Value}。");

    public PlanningFund GetPlanningFund(PlanningFundId id) =>
        _planningFunds.Find(f => f.Id == id) ?? throw new DomainException($"找不到財務規劃帳戶 {id.Value}。");

    public Category GetCategory(CategoryId id) =>
        _categories.Find(c => c.Id == id) ?? throw new DomainException($"找不到分類 {id.Value}。");

    public Account? FindAccount(string name) => _accounts.Find(a => a.Name == name);

    public PlanningFund? FindPlanningFund(string name) => _planningFunds.Find(f => f.Name == name);

    public Category? FindCategory(string mainName, string? subName = null)
    {
        var main = _categories.Find(c => c.IsMain && c.Name == mainName);
        if (main is null || subName is null)
        {
            return main;
        }

        return _categories.Find(c => c.ParentId == main.Id && c.Name == subName);
    }

    private Category AddMainCategory(string name, CategoryKind kind, ExpenseNature? nature)
    {
        name = RequireName(name);
        if (FindCategory(name) is not null)
        {
            throw new DomainException($"主分類名稱「{name}」已存在。");
        }

        var category = new Category(CategoryId.New(), name, kind, nature, parentId: null);
        _categories.Add(category);
        return category;
    }

    private static string RequireName(string name) =>
        string.IsNullOrWhiteSpace(name) ? throw new DomainException("名稱不可空白。") : name.Trim();
}
