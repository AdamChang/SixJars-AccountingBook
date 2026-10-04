using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using SixJars.Domain.Transactions;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Infrastructure.Tests.Persistence;

public class SoftDeleteTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset DeletedAt = new(2026, 2, 1, 3, 4, 5, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>軟刪除（ADR 0006）：資料仍在 DB（備份要用），但一般查詢與計算器的快照都看不到。</summary>
    [Fact]
    public async Task Deleted_rows_are_hidden_by_default_but_kept()
    {
        var book = new Book("我的帳本", new DateOnly(2025, 12, 30));
        var bank = book.AddAccount("國泰世華銀行", AccountType.Bank);
        var salary = book.AddIncomeCategory("工作薪資1");
        var phone = book.AddSubCategory(book.AddExpenseCategory("固定支出", ExpenseNature.Fixed).Id, "行動電話費");
        var factory = new TransactionFactory(book);
        var kept = factory.Income(new DateOnly(2026, 1, 5), bank.Id, salary.Id, 100m);
        var deleted = factory.Income(new DateOnly(2026, 1, 6), bank.Id, salary.Id, 200m);
        var keptPlan = PlannedExpense.Create(book, new BudgetMonth(2026, 1), phone.Id, bank.Id, -599m);
        var deletedPlan = PlannedExpense.Create(book, new BudgetMonth(2026, 1), phone.Id, bank.Id, -699m);
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        await using (var db = createContext())
        {
            db.Books.Add(book);
            db.Transactions.AddRange(kept, deleted);
            db.PlannedExpenses.AddRange(keptPlan, deletedPlan);
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = createContext())
        {
            (await db.Transactions.SingleAsync(t => t.Id == deleted.Id, Ct)).Delete(DeletedAt);
            (await db.PlannedExpenses.SingleAsync(p => p.Id == deletedPlan.Id, Ct)).Delete(DeletedAt);
            await db.SaveChangesAsync(Ct);
        }

        await using var readDb = createContext();
        (await readDb.Transactions.Select(t => t.Id).ToListAsync(Ct)).Should().Equal(kept.Id);
        (await readDb.PlannedExpenses.Select(p => p.Id).ToListAsync(Ct)).Should().Equal(keptPlan.Id);

        var allTransactions = await readDb.Transactions.IgnoreQueryFilters().AsNoTracking().ToListAsync(Ct);
        allTransactions.Select(t => t.Id).Should().BeEquivalentTo([kept.Id, deleted.Id]);
        var reloaded = allTransactions.Single(t => t.Id == deleted.Id);
        reloaded.DeletedAt.Should().Be(DeletedAt);
        reloaded.Postings.Should().NotBeEmpty("軟刪除保留分錄，備份才完整");
        var allPlans = await readDb.PlannedExpenses.IgnoreQueryFilters().AsNoTracking().ToListAsync(Ct);
        allPlans.Single(p => p.Id == deletedPlan.Id).DeletedAt.Should().Be(DeletedAt);

        var snapshot = await new LedgerSnapshotLoader(readDb).LoadAsync(book.Id, Ct);
        snapshot.Transactions.Select(t => t.Id).Should().Equal(kept.Id);
        snapshot.PlannedExpenses.Select(p => p.Id).Should().Equal(keptPlan.Id);
    }
}
