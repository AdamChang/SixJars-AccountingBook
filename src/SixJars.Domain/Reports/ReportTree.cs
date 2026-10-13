using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Transactions;

namespace SixJars.Domain.Reports;

public enum ReportSectionKind { Income, Floating, Fixed, Loan, Special }

/// <summary><c>Unassigned</c> 是「（未分子類）」：直接掛在主分類、而該主分類另有子分類列時的金額（spec §6.1）。</summary>
public enum ReportLineKind { Category, Unassigned, LoanAccount, PlanningFund }

/// <summary>樹上的一列；每格是一個月份（月報 1 格、年報 12 格），已換成報表的正數口徑。</summary>
internal sealed record ReportNode(
    ReportLineKind Kind, Guid Id, string Name, decimal[] Values, decimal[]? Principal, IReadOnlyList<ReportNode> Children)
{
    public bool IsEmpty =>
        Children.Count == 0 && Values.All(v => v == 0m) && (Principal is null || Principal.All(p => p == 0m));
}

internal sealed record ReportNodeSection(ReportSectionKind Kind, decimal[] Totals, IReadOnlyList<ReportNode> Lines);

internal sealed record ReportFundNode(PlanningFund Fund, decimal[] Allocation, decimal[] Withdrawal, decimal[] Return);

/// <summary>
/// spec §6.1 的歸組規則（ADR 0008），月報與年報共用（P4 M plan D1）。
/// 貸款繳款的利息分類、出資金的分類<b>不參與</b>歸組（Q-A）：預算 actual 因此恆等於浮動主分類的小計。
/// </summary>
internal sealed class ReportTree
{
    public const string UnassignedName = "（未分子類）";

    private static readonly (ReportSectionKind Section, ExpenseNature Nature)[] ExpenseSections =
    [
        (ReportSectionKind.Floating, ExpenseNature.Floating),
        (ReportSectionKind.Fixed, ExpenseNature.Fixed),
        (ReportSectionKind.Loan, ExpenseNature.Loan),
        (ReportSectionKind.Special, ExpenseNature.Special),
    ];

    private ReportTree(ReportNodeSection income, IReadOnlyList<ReportNodeSection> expenses, IReadOnlyList<ReportFundNode> funds)
    {
        Income = income;
        Expenses = expenses;
        Funds = funds;
    }

    public ReportNodeSection Income { get; }
    public IReadOnlyList<ReportNodeSection> Expenses { get; }
    public IReadOnlyList<ReportFundNode> Funds { get; }

    /// <param name="slotOf">歸屬月份 → 第幾格；不在報表範圍內回傳 null。</param>
    public static ReportTree Build(Book book, IEnumerable<ReportFlow> flows, int width, Func<BudgetMonth, int?> slotOf)
    {
        var byCategory = new Dictionary<CategoryId, decimal[]>();
        var loanTotals = new Dictionary<AccountId, decimal[]>();
        var loanPrincipals = new Dictionary<AccountId, decimal[]>();
        var allocations = new Dictionary<PlanningFundId, decimal[]>();
        var withdrawals = new Dictionary<PlanningFundId, decimal[]>();
        var returns = new Dictionary<PlanningFundId, decimal[]>();

        foreach (var flow in flows)
        {
            if (slotOf(flow.Month) is not { } slot)
            {
                continue;
            }

            switch (flow.Kind)
            {
                case TransactionKind.Income when flow.CategoryId is { } incomeCategory:
                    Cells(byCategory, incomeCategory, width)[slot] += flow.Amount;
                    break;
                case TransactionKind.Expense when flow.CategoryId is { } expense:
                    Cells(byCategory, expense, width)[slot] -= flow.Amount;
                    break;
                // 利息分類不參與歸組（Q-A）：一律依貸款帳戶列在「貸款」區
                case TransactionKind.LoanPayment when flow.CounterAccountId is { } loan:
                    Cells(loanTotals, loan, width)[slot] += flow.Amount;
                    Cells(loanPrincipals, loan, width)[slot] += flow.LoanPrincipal;
                    break;
                case TransactionKind.FundAllocation when flow.PlanningFundId is { } fund:
                    Cells(allocations, fund, width)[slot] += flow.Amount;
                    break;
                // 出資金帶的分類不參與歸組（Q-A）
                case TransactionKind.FundWithdrawal when flow.PlanningFundId is { } fund:
                    Cells(withdrawals, fund, width)[slot] += flow.Amount;
                    break;
                case TransactionKind.FundReturn when flow.PlanningFundId is { } fund:
                    Cells(returns, fund, width)[slot] += flow.Amount;
                    break;
            }
        }

        var incomeMains = book.Categories.Where(c => c.IsMain && c.Kind == CategoryKind.Income);
        var income = Section(ReportSectionKind.Income, width, CategoryLines(book, incomeMains, byCategory, width));

        var expenses = new List<ReportNodeSection>();
        foreach (var (section, nature) in ExpenseSections)
        {
            var lines = new List<ReportNode>();
            if (nature == ExpenseNature.Loan)
            {
                // 先列貸款帳戶，再列掛在貸款性質分類上的一般支出（D7）
                lines.AddRange(book.Accounts
                    .Where(a => loanTotals.ContainsKey(a.Id))
                    .OrderBy(a => a.SortOrder)
                    .Select(a => new ReportNode(ReportLineKind.LoanAccount, a.Id.Value, a.Name, loanTotals[a.Id], loanPrincipals[a.Id], []))
                    .Where(n => !n.IsEmpty));
            }

            var mains = book.Categories.Where(c => c.IsMain && c.Kind == CategoryKind.Expense && c.Nature == nature);
            lines.AddRange(CategoryLines(book, mains, byCategory, width));
            expenses.Add(Section(section, width, lines));
        }

        var funds = book.PlanningFunds
            .OrderBy(f => f.SortOrder)
            .Select(f => new ReportFundNode(
                f,
                allocations.GetValueOrDefault(f.Id) ?? new decimal[width],
                withdrawals.GetValueOrDefault(f.Id) ?? new decimal[width],
                returns.GetValueOrDefault(f.Id) ?? new decimal[width]))
            .Where(f => f.Allocation.Concat(f.Withdrawal).Concat(f.Return).Any(v => v != 0m))
            .ToList();

        return new ReportTree(income, expenses, funds);
    }

    /// <summary>支出分類（含子分類）歸到哪個區塊：依<b>主分類</b>的性質；收入分類回傳 null。</summary>
    public static ReportSectionKind? SectionOf(Book book, CategoryId categoryId)
    {
        var category = book.GetCategory(categoryId);
        var main = category.ParentId is { } parentId ? book.GetCategory(parentId) : category;
        return main.Nature switch
        {
            ExpenseNature.Floating => ReportSectionKind.Floating,
            ExpenseNature.Fixed => ReportSectionKind.Fixed,
            ExpenseNature.Loan => ReportSectionKind.Loan,
            ExpenseNature.Special => ReportSectionKind.Special,
            _ => null,
        };
    }

    private static List<ReportNode> CategoryLines(
        Book book, IEnumerable<Category> mains, Dictionary<CategoryId, decimal[]> cells, int width)
    {
        var lines = new List<ReportNode>();
        foreach (var main in mains.OrderBy(c => c.SortOrder))
        {
            var children = book.Categories
                .Where(c => c.ParentId == main.Id)
                .OrderBy(c => c.SortOrder)
                .Select(sub => new ReportNode(
                    ReportLineKind.Category, sub.Id.Value, sub.Name, cells.GetValueOrDefault(sub.Id) ?? new decimal[width], null, []))
                .Where(n => !n.IsEmpty)
                .ToList();
            var direct = cells.GetValueOrDefault(main.Id) ?? new decimal[width];
            // 「（未分子類）」只在有子分類列時才輸出；只有一層時它只是重複主分類的數字（D6）
            if (children.Count > 0 && direct.Any(v => v != 0m))
            {
                children.Add(new ReportNode(ReportLineKind.Unassigned, main.Id.Value, UnassignedName, direct, null, []));
            }

            var values = children.Count > 0 ? Sum(width, children.Select(c => c.Values)) : direct;
            var node = new ReportNode(ReportLineKind.Category, main.Id.Value, main.Name, values, null, children);
            if (!node.IsEmpty)
            {
                lines.Add(node);
            }
        }

        return lines;
    }

    private static ReportNodeSection Section(ReportSectionKind kind, int width, IReadOnlyList<ReportNode> lines) =>
        new(kind, Sum(width, lines.Select(l => l.Values)), lines);

    private static decimal[] Sum(int width, IEnumerable<decimal[]> rows)
    {
        var total = new decimal[width];
        foreach (var row in rows)
        {
            for (var i = 0; i < width; i++)
            {
                total[i] += row[i];
            }
        }

        return total;
    }

    private static decimal[] Cells<TKey>(Dictionary<TKey, decimal[]> map, TKey key, int width) where TKey : notnull
    {
        if (!map.TryGetValue(key, out var cells))
        {
            cells = new decimal[width];
            map[key] = cells;
        }

        return cells;
    }
}
