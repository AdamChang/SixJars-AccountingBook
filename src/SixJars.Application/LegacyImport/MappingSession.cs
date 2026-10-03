using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using SixJars.Domain.Transactions;

namespace SixJars.Application.LegacyImport;

/// <summary>
/// 一次匯入的狀態。流水帳（MappingSession.Journal.cs）與制式表格（MappingSession.Templates.cs）分檔實作。
/// 每一列各自 try-catch 是刻意的：批次匯入需要收集「全部」錯誤後一起回報。
/// </summary>
internal sealed partial class MappingSession(LegacyWorkbook workbook)
{
    private readonly ImportReport _report = new();
    private readonly List<Transaction> _transactions = [];
    private readonly List<PlannedExpense> _plannedExpenses = [];
    private Book _book = null!;
    private TransactionFactory _factory = null!;

    private LegacySettings Settings => workbook.Settings;

    public LegacyImportResult Run()
    {
        _book = new Book("我的帳本", OpeningDate());
        _factory = new TransactionFactory(_book);
        AddAccounts();
        AddPlanningFunds();
        AddCategories();

        foreach (var month in workbook.Months)
        {
            MapJournal(month);
        }

        return new LegacyImportResult(_book, _transactions, _plannedExpenses, _report);
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
        _book.AddAccount(LegacyNames.Cash, AccountType.Cash, Settings.HandCashOpening);
        var foreignCashOpening = Settings.PlanningFunds.FirstOrDefault(f => f.Name == LegacyNames.ForeignCashFund)?.Amount ?? 0m;
        _book.AddAccount(LegacyNames.ForeignCash, AccountType.Cash, foreignCashOpening, countsAsAvailableCash: false);

        AddAccounts(Settings.Banks, AccountType.Bank, sign: 1m);
        AddAccounts(Settings.CreditCards, AccountType.CreditCard, sign: -1m);
        AddAccounts(Settings.EWallets, AccountType.EWallet, sign: 1m);
        AddAccounts(Settings.Loans, AccountType.Loan, sign: -1m);
    }

    private void AddAccounts(IEnumerable<LegacyNamedAmount> items, AccountType type, decimal sign)
    {
        foreach (var item in items.Where(i => !LegacyNames.IsPlaceholder(i.Name)))
        {
            _book.AddAccount(item.Name, type, sign * item.Amount);
        }
    }

    private void AddPlanningFunds()
    {
        foreach (var fund in Settings.PlanningFunds.Where(f => !LegacyNames.IsPlaceholder(f.Name)))
        {
            _book.AddPlanningFund(fund.Name, fund.Amount);
        }
    }

    private void AddCategories()
    {
        foreach (var item in Settings.IncomeItems.Distinct())
        {
            var income = _book.AddIncomeCategory(item);
            if (item == LegacyNames.OtherIncome)
            {
                AddSubCategories(income, Settings.OtherIncomeSubItems);
            }
        }

        foreach (var list in workbook.FloatingCategories.Where(c => !LegacyNames.IsPlaceholder(c.Main)))
        {
            AddSubCategories(_book.AddExpenseCategory(list.Main, ExpenseNature.Floating), list.Subs);
        }

        AddSubCategories(_book.AddExpenseCategory(LegacyNames.FixedExpense, ExpenseNature.Fixed), Settings.FixedExpenseItems);
        AddSubCategories(_book.AddExpenseCategory(LegacyNames.LoanExpense, ExpenseNature.Loan), Settings.Loans.Select(l => l.Name));
        _book.AddExpenseCategory(LegacyNames.SpecialExpense, ExpenseNature.Special);
    }

    private void AddSubCategories(Category main, IEnumerable<string> names)
    {
        foreach (var name in names.Where(n => !LegacyNames.IsPlaceholder(n)).Distinct())
        {
            _book.AddSubCategory(main.Id, name);
        }
    }

    private BudgetMonth MonthOf(LegacyMonthSheet sheet) => new(Settings.Year, sheet.Month);

    private static string SheetName(LegacyMonthSheet sheet) => $"{sheet.Month}月";

    /// <summary>該列無法匯入；由逐列的 catch 記入錯誤清單。</summary>
    private sealed class RowRejected(string message) : Exception(message);
}
