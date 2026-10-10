using SixJars.Domain.Common;

namespace SixJars.Domain.Books;

/// <summary>帳本：連續不分年度的記帳邊界（ADR 0002），也是帳戶、財務規劃帳戶與分類的聚合根。</summary>
public sealed class Book
{
    private readonly List<Account> _accounts = [];
    private readonly List<PlanningFund> _planningFunds = [];
    private readonly List<Category> _categories = [];

    /// <param name="id">只有還原備份（T42）時指定，保留原本的 Id；一般新增時省略，自動產生。</param>
    public Book(string name, DateOnly openingDate, BookId? id = null)
    {
        Id = id ?? BookId.New();
        Name = RequireName(name);
        OpeningDate = openingDate;
    }

    // EF Core 專用：公開建構子的 id 參數型別（BookId?）與屬性不同，EF 無法綁定。
    private Book() => Name = null!;

    public BookId Id { get; private set; }
    public string Name { get; private set; }
    /// <summary>期初餘額的基準日；所有帳戶的期初餘額都視為此日結束時的餘額。</summary>
    public DateOnly OpeningDate { get; private set; }

    /// <summary>此日（含）以前的交易不可再新增、修改或刪除（CONTEXT.md 鎖帳日）。可前移、後移或清除（spec §9 O3）。</summary>
    public DateOnly? LockDate { get; private set; }

    public IReadOnlyList<Account> Accounts => _accounts;
    public IReadOnlyList<PlanningFund> PlanningFunds => _planningFunds;
    public IReadOnlyList<Category> Categories => _categories;

    // 以下各 Add 方法的 id 參數：只有還原備份（T42）時指定，保留原本的 Id；一般新增時省略，自動產生。
    // 指定 Id 不會略過任何檢查（名稱、類型、兩層分類）；同一個 Id 重複出現時由資料庫的主鍵擋下。

    public Account AddAccount(string name, AccountType type, decimal openingBalance = 0m, bool countsAsAvailableCash = true, AccountId? id = null)
    {
        name = RequireName(name);
        if (FindAccount(name) is not null)
        {
            throw new DomainException($"帳戶名稱「{name}」已存在。");
        }

        var account = new Account(id ?? AccountId.New(), name, type, openingBalance, type == AccountType.Cash && countsAsAvailableCash,
            NextSortOrder(_accounts, a => a.SortOrder));
        _accounts.Add(account);
        return account;
    }

    public PlanningFund AddPlanningFund(string name, decimal openingBalance = 0m, PlanningFundId? id = null)
    {
        name = RequireName(name);
        if (FindPlanningFund(name) is not null)
        {
            throw new DomainException($"財務規劃帳戶名稱「{name}」已存在。");
        }

        var fund = new PlanningFund(id ?? PlanningFundId.New(), name, openingBalance, NextSortOrder(_planningFunds, f => f.SortOrder));
        _planningFunds.Add(fund);
        return fund;
    }

    public Category AddIncomeCategory(string name, CategoryId? id = null) => AddMainCategory(name, CategoryKind.Income, nature: null, id);

    public Category AddExpenseCategory(string name, ExpenseNature nature, CategoryId? id = null) =>
        AddMainCategory(name, CategoryKind.Expense, nature, id);

    public Category AddSubCategory(CategoryId parentId, string name, CategoryId? id = null)
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

        var category = new Category(id ?? CategoryId.New(), name, parent.Kind, parent.Nature, parentId,
            NextSortOrder(SubCategoriesOf(parentId), c => c.SortOrder));
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

    // 改名沿用新增時的唯一性規則；改成原本的名字不算重複。

    public void RenameAccount(AccountId id, string name)
    {
        var account = GetAccount(id);
        name = RequireName(name);
        if (FindAccount(name) is { } other && other.Id != id)
        {
            throw new DomainException($"帳戶名稱「{name}」已存在。");
        }

        account.Rename(name);
    }

    public void RenamePlanningFund(PlanningFundId id, string name)
    {
        var fund = GetPlanningFund(id);
        name = RequireName(name);
        if (FindPlanningFund(name) is { } other && other.Id != id)
        {
            throw new DomainException($"財務規劃帳戶名稱「{name}」已存在。");
        }

        fund.Rename(name);
    }

    public void RenameCategory(CategoryId id, string name)
    {
        var category = GetCategory(id);
        name = RequireName(name);
        var duplicate = category.IsMain
            ? _categories.Find(c => c.IsMain && c.Name == name && c.Id != id)
            : _categories.Find(c => c.ParentId == category.ParentId && c.Name == name && c.Id != id);
        if (duplicate is not null)
        {
            throw new DomainException(category.IsMain
                ? $"主分類名稱「{name}」已存在。"
                : $"主分類「{GetCategory(category.ParentId!.Value).Name}」底下已有子分類「{name}」。");
        }

        category.Rename(name);
    }

    /// <summary>與新增時相同：只有現金帳戶可以計入可用現金。</summary>
    public void SetCountsAsAvailableCash(AccountId id, bool countsAsAvailableCash)
    {
        var account = GetAccount(id);
        if (countsAsAvailableCash && account.Type != AccountType.Cash)
        {
            throw new DomainException($"「{account.Name}」不是現金帳戶，不能計入可用現金。");
        }

        account.SetCountsAsAvailableCash(countsAsAvailableCash);
    }

    /// <param name="balance">由呼叫端以分錄算出的目前餘額（Domain 不查詢資料庫）。</param>
    public void ArchiveAccount(AccountId id, decimal balance, DateTimeOffset at)
    {
        var account = GetAccount(id);
        EnsureZeroBalance(account.Name, balance);
        account.Archive(at);
    }

    public void UnarchiveAccount(AccountId id) => GetAccount(id).Unarchive();

    /// <param name="balance">由呼叫端以分錄算出的目前餘額（Domain 不查詢資料庫）。</param>
    public void ArchivePlanningFund(PlanningFundId id, decimal balance, DateTimeOffset at)
    {
        var fund = GetPlanningFund(id);
        EnsureZeroBalance(fund.Name, balance);
        fund.Archive(at);
    }

    public void UnarchivePlanningFund(PlanningFundId id) => GetPlanningFund(id).Unarchive();

    /// <summary>封存主分類不改變子分類的狀態；子分類是否可選 = 自己與主分類都未封存（spec §3.1）。</summary>
    public void ArchiveCategory(CategoryId id, DateTimeOffset at) => GetCategory(id).Archive(at);

    public void UnarchiveCategory(CategoryId id) => GetCategory(id).Unarchive();

    // 排序清單必須恰好等於組內目前的成員（含已封存）：不能多、不能少、不能重複，擋下過時畫面造成的漏項。

    public void ReorderAccounts(IReadOnlyList<AccountId> ids) =>
        Reorder(_accounts, ids, a => a.Id, (a, order) => a.MoveTo(order), "帳戶");

    public void ReorderPlanningFunds(IReadOnlyList<PlanningFundId> ids) =>
        Reorder(_planningFunds, ids, f => f.Id, (f, order) => f.MoveTo(order), "財務規劃帳戶");

    /// <summary><paramref name="parentId"/> 有值時排序該主分類的子分類；否則排序 <paramref name="kind"/> 的主分類。</summary>
    public void ReorderCategories(CategoryKind kind, CategoryId? parentId, IReadOnlyList<CategoryId> ids)
    {
        var group = parentId is { } parent ? SubCategoriesOf(GetCategory(parent).Id).ToList() : MainCategoriesOf(kind).ToList();
        Reorder(group, ids, c => c.Id, (c, order) => c.MoveTo(order), "分類");
    }

    // 刪除後把組內剩下的項目重新編號，維持 SortOrder 0..n-1 連續（還原備份時才能重建出相同的順序）。

    /// <param name="isReferenced">由呼叫端查詢：是否有交易或預定支出（含已軟刪除）使用這個帳戶。</param>
    public void RemoveAccount(AccountId id, bool isReferenced)
    {
        var account = GetAccount(id);
        EnsureUnreferenced(account.Name, isReferenced);
        _accounts.Remove(account);
        Compact(_accounts, a => a.SortOrder, (a, order) => a.MoveTo(order));
    }

    /// <param name="isReferenced">由呼叫端查詢：是否有交易（含已軟刪除）使用這個財務規劃帳戶。</param>
    public void RemovePlanningFund(PlanningFundId id, bool isReferenced)
    {
        var fund = GetPlanningFund(id);
        EnsureUnreferenced(fund.Name, isReferenced);
        _planningFunds.Remove(fund);
        Compact(_planningFunds, f => f.SortOrder, (f, order) => f.MoveTo(order));
    }

    /// <param name="isReferenced">由呼叫端查詢：是否有交易或預定支出（含已軟刪除）使用這個分類。</param>
    /// <param name="hasBudget">由呼叫端查詢：是否有預算。另給訊息：預算可以清除，不必改用封存（P4 L plan D7）。</param>
    public void RemoveCategory(CategoryId id, bool isReferenced, bool hasBudget = false)
    {
        var category = GetCategory(id);
        if (category.IsMain && SubCategoriesOf(id).Any())
        {
            throw new DomainException($"「{category.Name}」底下還有子分類，請先刪除子分類。", DomainException.InUseCode);
        }

        EnsureUnreferenced(category.Name, isReferenced);
        if (hasBudget)
        {
            throw new DomainException($"「{category.Name}」有預算，請先清除預算再刪除。", DomainException.InUseCode);
        }

        _categories.Remove(category);
        var siblings = category.ParentId is { } parentId ? SubCategoriesOf(parentId) : MainCategoriesOf(category.Kind);
        Compact(siblings, c => c.SortOrder, (c, order) => c.MoveTo(order));
    }

    /// <summary>
    /// 改支出主分類的性質，回溯生效，子分類一起改（spec §3.1、Q4）。
    /// 預定支出只允許固定、貸款、特別，所以有預定支出（含已刪除）時不能改成浮動。
    /// 週期預定支出只允許固定、貸款，所以有週期項目時只能在這兩者之間切換（P4 K plan D6）。
    /// 預算只允許浮動，所以有預算時不能改成任何其他性質，必須先清除預算（spec Q4、P4 L plan L7）。
    /// </summary>
    public void ChangeExpenseNature(
        CategoryId id, ExpenseNature nature, bool hasPlannedExpenses, bool hasRecurringPlannedExpenses = false, bool hasBudget = false)
    {
        var category = GetCategory(id);
        if (!category.IsMain || category.Kind != CategoryKind.Expense)
        {
            throw new DomainException($"只有支出主分類可以修改支出性質，「{category.Name}」不是。");
        }

        if (category.Nature == nature)
        {
            return;
        }

        if (nature == ExpenseNature.Floating && hasPlannedExpenses)
        {
            throw new DomainException($"「{category.Name}」有預定支出，不能改成浮動支出。", DomainException.InUseCode);
        }

        if (nature is not (ExpenseNature.Fixed or ExpenseNature.Loan) && hasRecurringPlannedExpenses)
        {
            throw new DomainException($"「{category.Name}」有週期預定支出，只能是固定或貸款支出。", DomainException.InUseCode);
        }

        if (hasBudget)
        {
            throw new DomainException($"「{category.Name}」有預算，請先清除預算再修改支出性質。", DomainException.InUseCode);
        }

        category.ChangeNature(nature);
        foreach (var sub in SubCategoriesOf(id))
        {
            sub.ChangeNature(nature);
        }
    }

    public Account GetAccount(AccountId id) =>
        _accounts.Find(a => a.Id == id) ?? throw new DomainException($"找不到帳戶 {id.Value}。");

    public PlanningFund GetPlanningFund(PlanningFundId id) =>
        _planningFunds.Find(f => f.Id == id) ?? throw new DomainException($"找不到財務規劃帳戶 {id.Value}。");

    public Category GetCategory(CategoryId id) =>
        _categories.Find(c => c.Id == id) ?? throw new DomainException($"找不到分類 {id.Value}。");

    /// <summary>分類自己或它的主分類已封存（spec §3.1：子分類是否可選 = 自己與主分類都未封存）。</summary>
    public bool IsCategoryArchived(CategoryId id)
    {
        var category = GetCategory(id);
        return category.IsArchived || (category.ParentId is { } parentId && GetCategory(parentId).IsArchived);
    }

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

    // 排序的組：帳戶一組、財務規劃帳戶一組、同一種類的主分類一組、同一個主分類的子分類一組（spec §3.1）。
    private IEnumerable<Category> MainCategoriesOf(CategoryKind kind) => _categories.Where(c => c.IsMain && c.Kind == kind);

    private IEnumerable<Category> SubCategoriesOf(CategoryId parentId) => _categories.Where(c => c.ParentId == parentId);

    private static int NextSortOrder<T>(IEnumerable<T> group, Func<T, int> sortOrderOf) =>
        group.Select(sortOrderOf).DefaultIfEmpty(-1).Max() + 1;

    private Category AddMainCategory(string name, CategoryKind kind, ExpenseNature? nature, CategoryId? id)
    {
        name = RequireName(name);
        if (FindCategory(name) is not null)
        {
            throw new DomainException($"主分類名稱「{name}」已存在。");
        }

        var category = new Category(id ?? CategoryId.New(), name, kind, nature, parentId: null,
            NextSortOrder(MainCategoriesOf(kind), c => c.SortOrder));
        _categories.Add(category);
        return category;
    }

    private static void Reorder<TItem, TId>(IReadOnlyCollection<TItem> group, IReadOnlyList<TId> ids,
        Func<TItem, TId> idOf, Action<TItem, int> moveTo, string groupName) where TId : notnull
    {
        if (ids.Count != group.Count || !group.Select(idOf).ToHashSet().SetEquals(ids))
        {
            throw new DomainException($"{groupName}的排序清單與目前的項目不一致，請重新載入後再試。");
        }

        var byId = group.ToDictionary(idOf);
        for (var order = 0; order < ids.Count; order++)
        {
            moveTo(byId[ids[order]], order);
        }
    }

    private static void EnsureUnreferenced(string name, bool isReferenced)
    {
        if (isReferenced)
        {
            throw new DomainException($"「{name}」已有交易或預定支出使用，不能刪除；可以改用封存。", DomainException.InUseCode);
        }
    }

    private static void Compact<T>(IEnumerable<T> group, Func<T, int> sortOrderOf, Action<T, int> moveTo)
    {
        var order = 0;
        foreach (var item in group.OrderBy(sortOrderOf).ToList())
        {
            moveTo(item, order++);
        }
    }

    private static void EnsureZeroBalance(string name, decimal balance)
    {
        if (balance != 0m)
        {
            throw new DomainException($"「{name}」的餘額為 {balance:#,0.####}，餘額為 0 才能封存。", DomainException.NonZeroBalanceCode);
        }
    }

    private static string RequireName(string name) =>
        string.IsNullOrWhiteSpace(name) ? throw new DomainException("名稱不可空白。") : name.Trim();
}
