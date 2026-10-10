using SixJars.Domain.Books;
using SixJars.Domain.Common;

namespace SixJars.Domain.Planning;

/// <param name="DefaultAmount">預設值；本月有覆寫值時也照樣帶出，給行內編輯預設值用（P4 L plan D8）。</param>
/// <param name="Budget">本月適用的預算；null 表示未設（Q1a）。</param>
/// <param name="Actual">本月實際支出，正數（退款會抵銷）。</param>
/// <param name="Remaining">Budget − Actual；未設預算時為 null。</param>
public sealed record BudgetRow(CategoryId CategoryId, decimal? DefaultAmount, decimal? Budget, BudgetSource? Source, decimal Actual, decimal? Remaining);

/// <summary>只加總有設預算的列（P4 L plan D6）。</summary>
public sealed record BudgetTotals(decimal Budget, decimal Actual, decimal Remaining);

public sealed record BudgetSheetResult(IReadOnlyList<BudgetRow> Rows, BudgetTotals Totals);

/// <summary>某個歸屬月份的預算表（spec §5.2）：查詢（GetBudgets）與驗收比對共用同一套規則。</summary>
public static class BudgetSheet
{
    /// <param name="expenseTotals">該月各分類（含子分類）支出交易的金額加總，沿用交易的符號（支出為負），
    /// 即 <c>ILedgerSummaryQuery.ExpenseTotalsByCategoryAsync</c> 的結果；不屬於浮動主分類的會被忽略。</param>
    public static BudgetSheetResult Build(
        Book book, BudgetMonth month, IEnumerable<CategoryBudget> budgets, IReadOnlyDictionary<CategoryId, decimal> expenseTotals)
    {
        var budgetByCategory = budgets.ToDictionary(b => b.CategoryId);
        var rows = new List<BudgetRow>();
        var floatingMains = book.Categories
            .Where(c => c.IsMain && c.Kind == CategoryKind.Expense && c.Nature == ExpenseNature.Floating)
            .OrderBy(c => c.SortOrder);
        foreach (var main in floatingMains)
        {
            var actual = -book.Categories
                .Where(c => c.Id == main.Id || c.ParentId == main.Id)
                .Sum(c => expenseTotals.GetValueOrDefault(c.Id));
            var budget = budgetByCategory.GetValueOrDefault(main.Id);
            // 已封存的分類只在該月有實際支出或覆寫值時列出（spec §5.2、Q14）。
            if (main.IsArchived && actual == 0m && budget?.HasOverride(month) != true)
            {
                continue;
            }

            var (amount, source) = budget is null ? ((decimal?)null, (BudgetSource?)null) : budget.AmountFor(month);
            rows.Add(new BudgetRow(main.Id, budget?.DefaultAmount, amount, source, actual, amount - actual));
        }

        var budgeted = rows.Where(r => r.Budget is not null).ToList();
        var totalBudget = budgeted.Sum(r => r.Budget!.Value);
        var totalActual = budgeted.Sum(r => r.Actual);
        return new BudgetSheetResult(rows, new BudgetTotals(totalBudget, totalActual, totalBudget - totalActual));
    }
}
