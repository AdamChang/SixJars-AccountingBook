using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using SixJars.Domain.Transactions;

namespace SixJars.Application.LegacyImport;

/// <summary>制式表格（固定、貸款、特別支出）→ 預定支出，貸款本金依貸款區分攤（spec §5.3–5.4）。必須在同月流水帳之後處理。</summary>
internal sealed partial class MappingSession
{
    private void MapTemplates(LegacyMonthSheet month)
    {
        foreach (var row in month.Templates.Where(t => t.Amount != 0m))
        {
            try
            {
                MapTemplateRow(month, row);
            }
            catch (Exception ex) when (ex is DomainException or RowRejected)
            {
                _report.AddError(SheetName(month), row.Row, ex.Message);
            }
        }
    }

    /// <summary>先建立預定支出與交易，全部成功後才一起加入結果，避免留下「已付卻沒有連結交易」的預定支出。</summary>
    private void MapTemplateRow(LegacyMonthSheet month, LegacyTemplateRow row)
    {
        var category = TemplateCategory(row);
        var account = row.Method is null ? null : ResolveAccount(row.Method);
        var planned = PlannedExpense.Create(_book, MonthOf(month), category.Id, account?.Id, row.Amount, row.Note);

        if (row.PaidDate is not { } paidDate)
        {
            _plannedExpenses.Add(planned);
            return;
        }

        if (account is null)
        {
            _report.AddWarning(SheetName(month), row.Row, $"「{row.Item}」有支出日但沒有方式，視為未付的預定支出（扣月可用餘額，不扣任何帳戶）。");
            _plannedExpenses.Add(planned);
            return;
        }

        var date = NormalizeDate(month, row.Row, paidDate);
        var transaction = row.Section == LegacyTemplateSection.Loan
            ? _factory.LoanPayment(
                date, account.Id, ResolveAccount(row.Item).Id, -row.Amount, TemplateLoanPrincipal(month, row),
                category.Id, row.Note, MonthOf(month))
            : _factory.Expense(date, account.Id, category.Id, row.Amount, row.Note, MonthOf(month));

        planned.MarkPaid(transaction);
        _transactions.Add(transaction);
        _plannedExpenses.Add(planned);
    }

    private Category TemplateCategory(LegacyTemplateRow row) => row.Section switch
    {
        LegacyTemplateSection.Fixed => RequireCategory(LegacyNames.FixedExpense, row.Item),
        LegacyTemplateSection.Loan => RequireCategory(LegacyNames.LoanExpense, row.Item),
        _ => _book.FindCategory(LegacyNames.SpecialExpense, row.Item)
            ?? _book.AddSubCategory(RequireCategory(LegacyNames.SpecialExpense, null).Id, row.Item),
    };

    /// <summary>制式表格的本金 = 當月本金總額 − 流水帳中同貸款已提前還的本金。</summary>
    private decimal TemplateLoanPrincipal(LegacyMonthSheet month, LegacyTemplateRow row)
    {
        var paidRows = month.Templates.Count(t =>
            t.Section == LegacyTemplateSection.Loan && t.Item == row.Item && t.Amount != 0m && t.PaidDate is not null && t.Method is not null);
        if (paidRows > 1)
        {
            throw new RowRejected($"同月「{row.Item}」有 {paidRows} 筆已付的貸款列，無法分攤本金。");
        }

        var loan = ResolveAccount(row.Item);
        var budgetMonth = MonthOf(month);
        var monthPrincipal = Math.Abs(month.LoanPrincipals.FirstOrDefault(p => p.Name == row.Item)?.Amount ?? 0m);
        var journalPrincipal = _transactions
            .Where(t => t.Kind == TransactionKind.LoanPayment && t.BudgetMonth == budgetMonth && t.CounterAccountId == loan.Id)
            .Sum(t => t.LoanPrincipal ?? 0m);
        var remaining = monthPrincipal - journalPrincipal;
        var total = -row.Amount;

        if (remaining < 0m || remaining > total)
        {
            throw new RowRejected($"「{row.Item}」本金無法分攤：當月本金 {monthPrincipal}，流水帳已還本金 {journalPrincipal}，制式表格繳款 {total}。");
        }

        return remaining;
    }
}
