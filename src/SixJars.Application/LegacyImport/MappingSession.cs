using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using SixJars.Domain.Transactions;

namespace SixJars.Application.LegacyImport;

/// <summary>
/// 一次匯入的狀態。流水帳（MappingSession.Journal.cs）與制式表格（MappingSession.Templates.cs）分檔實作。
/// 每一列各自 try-catch 是刻意的：批次匯入需要收集「全部」錯誤後一起回報。
/// </summary>
internal sealed partial class MappingSession(LegacyWorkbook workbook, string bookName)
{
    /// <summary>設定項目的錯誤沒有列號，一律記在「設定」工作表第 0 列。</summary>
    private const string SettingsSheet = "設定";

    private readonly ImportReport _report = new();
    private readonly List<Transaction> _transactions = [];
    private readonly List<PlannedExpense> _plannedExpenses = [];
    private readonly Dictionary<AccountId, decimal> _accountOpenings = [];
    private readonly Dictionary<PlanningFundId, decimal> _fundOpenings = [];
    private Book _book = null!;
    private TransactionFactory _factory = null!;

    /// <summary>補匯舊年度：對應到既有帳本，同名項目沿用、不改期初（spec D3）。</summary>
    private bool _intoExisting;

    private LegacySettings Settings => workbook.Settings;

    public LegacyImportResult Run()
    {
        MapAll(new Book(bookName, OpeningDate()));
        return new LegacyImportResult(_book, _transactions, _plannedExpenses, _report);
    }

    /// <summary>
    /// 把舊年度對應到既有帳本（補匯舊年度，spec D3）：同名項目沿用，舊年度的期初另外收集、不寫進既有項目；
    /// 只在舊年度出現的項目會加入帳本（未封存），由呼叫端在檢查通過後封存。
    /// </summary>
    public LegacyPrependMapping RunInto(Book target)
    {
        _intoExisting = true;
        var accounts = target.Accounts.Select(a => a.Id).ToHashSet();
        var funds = target.PlanningFunds.Select(f => f.Id).ToHashSet();
        var categories = target.Categories.Select(c => c.Id).ToHashSet();

        MapAll(target);

        // 取差集而不是在每個新增點記錄：流水帳與制式表格過程中自動補上的子分類也要算進去。
        return new LegacyPrependMapping(
            OpeningDate(), _transactions, _plannedExpenses, _accountOpenings, _fundOpenings,
            target.Accounts.Select(a => a.Id).Where(id => !accounts.Contains(id)).ToHashSet(),
            target.PlanningFunds.Select(f => f.Id).Where(id => !funds.Contains(id)).ToHashSet(),
            target.Categories.Select(c => c.Id).Where(id => !categories.Contains(id)).ToHashSet(),
            _report);
    }

    private void MapAll(Book book)
    {
        _book = book;
        _factory = new TransactionFactory(_book);
        AddAccounts();
        AddPlanningFunds();
        AddCategories();

        foreach (var month in workbook.Months)
        {
            MapJournal(month);
            MapTemplates(month);
        }
    }

    /// <summary>期初日 = 修正後最早日期的前一天；沒有任何日期時為該年 1/1 的前一天。</summary>
    private DateOnly OpeningDate()
    {
        var dates = workbook.Months.SelectMany(month =>
            month.Journal.Select(row => row.Date)
                .Concat(month.Templates.Select(row => row.PaidDate))
                .OfType<DateOnly>()
                .Select(date => LegacyDateNormalizer.Normalize(date, MonthOf(month)).Date))
            .ToList();

        var earliest = dates.Count > 0 ? dates.Min() : new DateOnly(Settings.Year, 1, 1);
        return earliest.AddDays(-1);
    }

    private void AddAccounts()
    {
        AddAccount(LegacyNames.Cash, AccountType.Cash, Settings.HandCashOpening);
        var foreignCashOpening = Settings.PlanningFunds.FirstOrDefault(f => f.Name == LegacyNames.ForeignCashFund)?.Amount ?? 0m;
        AddAccount(LegacyNames.ForeignCash, AccountType.Cash, foreignCashOpening, countsAsAvailableCash: false);

        AddAccounts(Settings.Banks, AccountType.Bank, sign: 1m);
        AddAccounts(Settings.CreditCards, AccountType.CreditCard, sign: -1m);
        AddAccounts(Settings.EWallets, AccountType.EWallet, sign: 1m);
        AddAccounts(Settings.Loans, AccountType.Loan, sign: -1m);
    }

    private void AddAccounts(IEnumerable<LegacyNamedAmount> items, AccountType type, decimal sign)
    {
        foreach (var item in items.Where(i => !LegacyNames.IsPlaceholder(i.Name)))
        {
            AddAccount(item.Name, type, sign * item.Amount);
        }
    }

    private void AddAccount(string name, AccountType type, decimal openingBalance, bool countsAsAvailableCash = true)
    {
        if (!_intoExisting)
        {
            _book.AddAccount(name, type, openingBalance, countsAsAvailableCash);
            return;
        }

        var account = _book.FindAccount(name.Trim()) ?? _book.AddAccount(name, type, openingBalance, countsAsAvailableCash);
        if (account.Type != type)
        {
            // 名稱相同但類型不同（spec D4）：錢不可能從銀行變成信用卡負債。記錯誤、不收集期初，整批不會寫入。
            _report.AddError(SettingsSheet, 0, $"帳戶「{account.Name}」在舊年度是 {type}，帳本裡是 {account.Type}，無法銜接。");
            return;
        }

        _accountOpenings[account.Id] = openingBalance;
    }

    private void AddPlanningFunds()
    {
        foreach (var fund in Settings.PlanningFunds.Where(f => !LegacyNames.IsPlaceholder(f.Name)))
        {
            AddPlanningFund(fund.Name, fund.Amount);
        }
    }

    private void AddPlanningFund(string name, decimal openingBalance)
    {
        if (!_intoExisting)
        {
            _book.AddPlanningFund(name, openingBalance);
            return;
        }

        var fund = _book.FindPlanningFund(name.Trim()) ?? _book.AddPlanningFund(name, openingBalance);
        _fundOpenings[fund.Id] = openingBalance;
    }

    private void AddCategories()
    {
        foreach (var item in Settings.IncomeItems.Distinct())
        {
            var income = AddMainCategory(item, CategoryKind.Income, nature: null);
            if (item == LegacyNames.OtherIncome)
            {
                AddSubCategories(income, Settings.OtherIncomeSubItems);
            }
        }

        foreach (var list in workbook.FloatingCategories.Where(c => !LegacyNames.IsPlaceholder(c.Main)))
        {
            AddSubCategories(AddMainCategory(list.Main, CategoryKind.Expense, ExpenseNature.Floating), list.Subs);
        }

        AddSubCategories(AddMainCategory(LegacyNames.FixedExpense, CategoryKind.Expense, ExpenseNature.Fixed), Settings.FixedExpenseItems);
        AddSubCategories(AddMainCategory(LegacyNames.LoanExpense, CategoryKind.Expense, ExpenseNature.Loan), Settings.Loans.Select(l => l.Name));
        AddMainCategory(LegacyNames.SpecialExpense, CategoryKind.Expense, ExpenseNature.Special);
    }

    private Category AddMainCategory(string name, CategoryKind kind, ExpenseNature? nature)
    {
        if ((_intoExisting ? _book.FindCategory(name.Trim()) : null) is not { } existing)
        {
            return kind == CategoryKind.Income ? _book.AddIncomeCategory(name) : _book.AddExpenseCategory(name, nature!.Value);
        }

        if (existing.Kind != kind)
        {
            _report.AddError(SettingsSheet, 0, $"主分類「{existing.Name}」在舊年度是 {kind}，帳本裡是 {existing.Kind}，無法銜接。");
        }
        else if (existing.Nature != nature)
        {
            // 以帳本為準（R3-Q3）：使用者在系統裡整理過的設定不被舊資料改掉。
            _report.AddWarning(SettingsSheet, 0, $"主分類「{existing.Name}」在舊年度是 {nature}，帳本裡是 {existing.Nature}；以帳本為準。");
        }

        return existing;
    }

    private void AddSubCategories(Category main, IEnumerable<string> names)
    {
        foreach (var name in names.Where(n => !LegacyNames.IsPlaceholder(n)).Distinct())
        {
            if (_intoExisting && _book.FindCategory(main.Name, name.Trim()) is not null)
            {
                continue;
            }

            _book.AddSubCategory(main.Id, name);
        }
    }

    private BudgetMonth MonthOf(LegacyMonthSheet sheet) => new(Settings.Year, sheet.Month);

    private static string SheetName(LegacyMonthSheet sheet) => $"{sheet.Month}月";

    /// <summary>該列無法匯入；由逐列的 catch 記入錯誤清單。</summary>
    private sealed class RowRejected(string message) : Exception(message);
}
