using Microsoft.EntityFrameworkCore;
using SixJars.Application.Ledger;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Transactions;
using SixJars.Infrastructure.Persistence;

namespace SixJars.Infrastructure.Ledger;

/// <summary>
/// 以 EF LINQ 在 PostgreSQL 端彙總（spec §6），每個方法各翻成單一條 <c>GROUP BY</c>（月可用餘額為兩條）。
/// 規則照搬 P1 的 <c>BalanceCalculator</c> 與 <c>DisposableBalanceCalculator</c>，由 oracle 測試鎖住。
/// </summary>
/// <remarks>
/// 已刪除的交易與預定支出靠 EF 的 global query filter 排除，因為這裡全部經由 <c>DbSet</c> 查詢。
/// <b>如果將來改用 <c>FromSql</c> 等 raw SQL，filter 不會套用，必須自行加上 <c>"DeletedAt" IS NULL</c></b>（ADR 0006）。
/// </remarks>
public sealed class SqlLedgerSummaryQuery(SixJarsDbContext db) : ILedgerSummaryQuery
{
    public async Task<IReadOnlyDictionary<AccountId, decimal>> PostingTotalsAsync(
        BookId bookId, BalanceCutoff cutoff, CancellationToken cancellationToken)
    {
        var totals = await Through(db.Transactions.Where(t => t.BookId == bookId), cutoff)
            .SelectMany(t => t.Postings)
            .GroupBy(p => p.AccountId)
            .Select(g => new { AccountId = g.Key, Total = g.Sum(p => p.Amount) })
            .ToListAsync(cancellationToken);
        return totals.ToDictionary(x => x.AccountId, x => x.Total);
    }

    public async Task<IReadOnlyDictionary<PlanningFundId, decimal>> FundDeltaTotalsAsync(
        BookId bookId, BalanceCutoff cutoff, CancellationToken cancellationToken)
    {
        var totals = await Through(db.Transactions.Where(t => t.BookId == bookId && t.PlanningFundId != null), cutoff)
            .GroupBy(t => new { t.PlanningFundId, t.Kind })
            .Select(g => new { g.Key.PlanningFundId, g.Key.Kind, Total = g.Sum(t => t.Amount) })
            .ToListAsync(cancellationToken);

        // 符號與 Transaction.FundDelta 相同：入新資金與資金回流為正、出資金為負。
        return totals
            .GroupBy(x => x.PlanningFundId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Kind switch
            {
                TransactionKind.FundAllocation or TransactionKind.FundReturn => x.Total,
                TransactionKind.FundWithdrawal => -x.Total,
                _ => 0m,
            }));
    }

    public async Task<IReadOnlyDictionary<BudgetMonth, decimal>> MonthlyDisposableAsync(
        Book book, BudgetMonth from, BudgetMonth to, CancellationToken cancellationToken)
    {
        var bookId = book.Id;
        var walletIds = book.Accounts.Where(a => a.Type == AccountType.EWallet).Select(a => a.Id).ToList();

        // 電子錢包的消費已在加值時扣過，不可再扣一次（Q5）：在 SQL 端以 CASE WHEN 排除。
        var flows = await db.Transactions
            .Where(t => t.BookId == bookId && t.BudgetMonth >= from && t.BudgetMonth <= to)
            .GroupBy(t => new { t.BudgetMonth, t.Kind })
            .Select(g => new
            {
                g.Key.BudgetMonth,
                g.Key.Kind,
                Total = g.Sum(t => t.Kind == TransactionKind.Expense && walletIds.Contains(t.AccountId) ? 0m : t.Amount),
            })
            .ToListAsync(cancellationToken);

        // IsPaid 是計算屬性，EF 翻譯不了，改用 PaidTransactionId 判斷。
        var unpaid = await db.PlannedExpenses
            .Where(p => p.BookId == bookId && p.PaidTransactionId == null && p.BudgetMonth >= from && p.BudgetMonth <= to)
            .GroupBy(p => p.BudgetMonth)
            .Select(g => new { BudgetMonth = g.Key, Total = g.Sum(p => p.EstimatedAmount) })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<BudgetMonth, decimal>();
        for (var month = from; month <= to; month = Next(month))
        {
            result[month] = 0m;
        }

        foreach (var flow in flows)
        {
            result[flow.BudgetMonth] += Impact(flow.Kind, flow.Total);
        }

        foreach (var plan in unpaid)
        {
            result[plan.BudgetMonth] += plan.Total;
        }

        return result;
    }

    /// <summary>依截止方式篩選交易；兩種都含截止當天／當月。</summary>
    private static IQueryable<Transaction> Through(IQueryable<Transaction> transactions, BalanceCutoff cutoff) => cutoff switch
    {
        BalanceCutoff.AsOf(var date) => transactions.Where(t => t.Date <= date),
        BalanceCutoff.ThroughBudgetMonth(var month) => transactions.Where(t => t.BudgetMonth <= month),
        _ => throw new ArgumentOutOfRangeException(nameof(cutoff), cutoff, "不支援的餘額截止方式。"),
    };

    /// <summary>P1 §4.1 的正負號：收入 + 支出 − 加值 − 入新資金 − 貸款繳款總額；其餘類型不影響。</summary>
    private static decimal Impact(TransactionKind kind, decimal total) => kind switch
    {
        TransactionKind.Income or TransactionKind.Expense => total,
        TransactionKind.TopUp or TransactionKind.FundAllocation or TransactionKind.LoanPayment => -total,
        _ => 0m,
    };

    private static BudgetMonth Next(BudgetMonth month) =>
        month.Month == 12 ? new BudgetMonth(month.Year + 1, 1) : new BudgetMonth(month.Year, month.Month + 1);
}
