using FluentAssertions;
using SixJars.Domain.Books;
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
        var (mine, myIncome) = BookWithOneIncome("我的帳本");
        var (other, otherIncome) = BookWithOneIncome("別人的帳本");
        var createContext = await postgres.CreateDatabaseAsync(ct);
        await using (var db = createContext())
        {
            db.Books.AddRange(mine, other);
            db.Transactions.AddRange(myIncome, otherIncome);
            await db.SaveChangesAsync(ct);
        }

        await using var readDb = createContext();
        var snapshot = await new LedgerSnapshotLoader(readDb).LoadAsync(mine.Id, ct);

        snapshot.Book.Id.Should().Be(mine.Id);
        snapshot.Transactions.Select(t => t.Id).Should().Equal(myIncome.Id);
        snapshot.PlannedExpenses.Should().BeEmpty();
    }

    private static (Book, Transaction) BookWithOneIncome(string name)
    {
        var book = new Book(name, new DateOnly(2025, 12, 30));
        var bank = book.AddAccount("國泰世華銀行", AccountType.Bank);
        var salary = book.AddIncomeCategory("工作薪資1");
        return (book, new TransactionFactory(book).Income(new DateOnly(2026, 1, 5), bank.Id, salary.Id, 100m));
    }
}
