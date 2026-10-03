using FluentAssertions;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using SixJars.Domain.Transactions;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Infrastructure.Tests.Persistence;

public class LedgerSnapshotLoaderTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Loads_only_the_requested_book()
    {
        var ct = TestContext.Current.CancellationToken;
        var (mine, myIncome, myPlan) = BookWithOneIncomeAndPlan("我的帳本");
        var (other, otherIncome, otherPlan) = BookWithOneIncomeAndPlan("別人的帳本");
        var createContext = await postgres.CreateDatabaseAsync(ct);
        await using (var db = createContext())
        {
            db.Books.AddRange(mine, other);
            db.Transactions.AddRange(myIncome, otherIncome);
            db.PlannedExpenses.AddRange(myPlan, otherPlan);
            await db.SaveChangesAsync(ct);
        }

        await using var readDb = createContext();
        var snapshot = await new LedgerSnapshotLoader(readDb).LoadAsync(mine.Id, ct);

        snapshot.Book.Id.Should().Be(mine.Id);
        snapshot.Transactions.Select(t => t.Id).Should().Equal(myIncome.Id);
        snapshot.PlannedExpenses.Select(p => p.Id).Should().Equal(myPlan.Id);
    }

    private static (Book, Transaction, PlannedExpense) BookWithOneIncomeAndPlan(string name)
    {
        var book = new Book(name, new DateOnly(2025, 12, 30));
        var bank = book.AddAccount("國泰世華銀行", AccountType.Bank);
        var salary = book.AddIncomeCategory("工作薪資1");
        var phone = book.AddSubCategory(book.AddExpenseCategory("固定支出", ExpenseNature.Fixed).Id, "行動電話費");
        var income = new TransactionFactory(book).Income(new DateOnly(2026, 1, 5), bank.Id, salary.Id, 100m);
        var plan = PlannedExpense.Create(book, new BudgetMonth(2026, 1), phone.Id, bank.Id, -599m);
        return (book, income, plan);
    }
}
