using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Transactions;

namespace SixJars.Application.LegacyImport;

/// <summary>流水帳列（各月 AH:AM）→ 交易，依 spec §5.2 的規則表。歸屬月份一律為工作表月份。</summary>
internal sealed partial class MappingSession
{
    private void MapJournal(LegacyMonthSheet month)
    {
        foreach (var row in month.Journal)
        {
            try
            {
                _transactions.Add(MapJournalRow(month, row));
            }
            catch (Exception ex) when (ex is DomainException or RowRejected)
            {
                _report.AddError(SheetName(month), row.Row, ex.Message);
            }
        }
    }

    private Transaction MapJournalRow(LegacyMonthSheet month, LegacyJournalRow row)
    {
        if (row.Date is not { } rawDate)
        {
            throw new RowRejected("缺少日期。");
        }

        var date = NormalizeDate(month, row.Row, rawDate);
        var account = ResolveAccount(row.Method);
        var budgetMonth = MonthOf(month);
        var x = row.Amount;

        switch (row.Main)
        {
            case LegacyNames.Transfer:
                return MapTransfer(date, account, ResolveAccount(row.Sub), x, row.Note, budgetMonth);
            case LegacyNames.Withdrawal:
                return _factory.Withdrawal(date, account.Id, CashAccount.Id, Math.Abs(x), row.Note, budgetMonth);
            case LegacyNames.CashDeposit:
                return _factory.CashDeposit(date, account.Id, CashAccount.Id, Math.Abs(x), row.Note, budgetMonth);
            case LegacyNames.TopUp:
                return _factory.TopUp(date, account.Id, ResolveAccount(row.Sub).Id, Math.Abs(x), row.Note, budgetMonth);
            case LegacyNames.CardPayment:
                return _factory.CardPayment(date, account.Id, ResolveAccount(row.Sub).Id, Math.Abs(x), row.Note, budgetMonth);
            case LegacyNames.LoanDisbursement:
                return _factory.LoanDisbursement(date, account.Id, ResolveAccount(row.Sub).Id, Math.Abs(x), row.Note, budgetMonth);
            case LegacyNames.LoanExpense:
                // 流水帳裡的貸款支出一律視為提前還本：本金 = 繳款全額（spec §5.4）。
                return _factory.LoanPayment(
                    date, account.Id, ResolveAccount(row.Sub).Id, Math.Abs(x), Math.Abs(x),
                    RequireCategory(LegacyNames.LoanExpense, row.Sub).Id, row.Note, budgetMonth);
            case LegacyNames.BankFee:
                return _factory.Expense(date, account.Id, RequireCategory(LegacyNames.Financial, LegacyNames.BankFee).Id, x, row.Note, budgetMonth);
            case { } main when _book.FindPlanningFund(main) is { } fund:
                return MapFund(date, account, fund, row, budgetMonth);
            default:
                return MapIncomeOrExpense(month, date, account, row, budgetMonth);
        }
    }

    private Transaction MapTransfer(DateOnly date, Account method, Account sub, decimal x, string? note, BudgetMonth budgetMonth) =>
        x > 0m
            ? _factory.Transfer(date, sub.Id, method.Id, x, note, budgetMonth)
            : _factory.Transfer(date, method.Id, sub.Id, -x, note, budgetMonth);

    private Transaction MapFund(DateOnly date, Account account, PlanningFund fund, LegacyJournalRow row, BudgetMonth budgetMonth)
    {
        var x = row.Amount;
        var subAccount = row.Sub is { } sub ? FindAccountByMethod(sub) : null;

        if (row.Method == LegacyNames.Cash && (row.Sub == LegacyNames.NewFunds || subAccount is not null))
        {
            throw new RowRejected($"不支援以現金撥入財務規劃帳戶「{fund.Name}」（Q26）。");
        }

        if (row.Sub == LegacyNames.FundWithdrawal && x < 0m)
        {
            return _factory.FundWithdrawal(date, fund.Id, account.Id, -x, note: row.Note, budgetMonth: budgetMonth);
        }

        if (row.Sub == LegacyNames.FundReturn && x > 0m)
        {
            return _factory.FundReturn(date, fund.Id, account.Id, x, row.Note, budgetMonth);
        }

        if (subAccount is not null && x > 0m)
        {
            return _factory.FundAllocation(date, fund.Id, account.Id, subAccount.Id, x, row.Note, budgetMonth);
        }

        throw new RowRejected($"無法辨識的財務規劃帳戶列：「{fund.Name}／{row.Sub}」，金額 {x}。");
    }

    private Transaction MapIncomeOrExpense(LegacyMonthSheet month, DateOnly date, Account account, LegacyJournalRow row, BudgetMonth budgetMonth)
    {
        if (row.Main is not { } mainName || _book.FindCategory(mainName) is not { } main)
        {
            throw new RowRejected($"未知的主選單「{row.Main}」。");
        }

        var category = main;
        if (row.Sub is { } subName)
        {
            category = _book.FindCategory(mainName, subName) ?? AddSubCategoryWithWarning(month, row.Row, main, subName);
        }

        return main.Kind == CategoryKind.Income
            ? _factory.Income(date, account.Id, category.Id, row.Amount, row.Note, budgetMonth)
            : _factory.Expense(date, account.Id, category.Id, row.Amount, row.Note, budgetMonth);
    }

    private Category AddSubCategoryWithWarning(LegacyMonthSheet month, int row, Category main, string subName)
    {
        _report.AddWarning(SheetName(month), row, $"子分類「{main.Name}／{subName}」不在清單中，已自動新增。");
        return _book.AddSubCategory(main.Id, subName);
    }

    private DateOnly NormalizeDate(LegacyMonthSheet month, int row, DateOnly date)
    {
        var (normalized, corrected) = LegacyDateNormalizer.Normalize(date, MonthOf(month));
        if (corrected)
        {
            _report.AddCorrection(SheetName(month), row, $"日期 {date:yyyy-MM-dd} 與工作表月份相差過大，已修正為 {normalized:yyyy-MM-dd}。");
        }

        return normalized;
    }

    private Account CashAccount => _book.FindAccount(LegacyNames.Cash)!;

    /// <summary>「現金」→ 現金帳戶；「非手上現金」→ 外幣現鈔；其他依名稱查找。</summary>
    private Account? FindAccountByMethod(string name) => name switch
    {
        LegacyNames.NonHandCash => _book.FindAccount(LegacyNames.ForeignCash),
        _ => _book.FindAccount(name),
    };

    private Account ResolveAccount(string? name) =>
        (name is null ? null : FindAccountByMethod(name)) ?? throw new RowRejected($"未知的方式「{name}」。");

    private Category RequireCategory(string mainName, string? subName) =>
        _book.FindCategory(mainName, subName) ?? throw new RowRejected($"找不到分類「{mainName}／{subName}」。");
}
