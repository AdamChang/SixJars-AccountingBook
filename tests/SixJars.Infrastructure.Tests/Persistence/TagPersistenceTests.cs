using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SixJars.Domain.Books;
using SixJars.Domain.Transactions;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Infrastructure.Tests.Persistence;

public class TagPersistenceTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset At = new(2026, 10, 19, 1, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tags_and_transaction_tags_round_trip()
    {
        var book = new Book("我的帳本", new DateOnly(2025, 12, 30));
        var cash = book.AddAccount("現金", AccountType.Cash);
        var food = book.AddExpenseCategory("飲食", ExpenseNature.Floating);
        var trip = book.AddTag("✈️日本旅遊", TagColor.Teal);
        var work = book.AddTag("可報帳", TagColor.Amber);
        book.ArchiveTag(work.Id, At);
        var factory = new TransactionFactory(book);
        var expense = factory.WithTags(factory.Expense(new DateOnly(2026, 1, 5), cash.Id, food.Id, -100m), [trip.Id, work.Id]);
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        await using (var db = createContext())
        {
            db.Books.Add(book);
            db.Transactions.Add(expense);
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = createContext())
        {
            var loadedBook = await db.Books.SingleAsync(b => b.Id == book.Id, Ct);
            loadedBook.Tags.Should().BeEquivalentTo(book.Tags);
            loadedBook.GetTag(work.Id).ArchivedAt.Should().Be(At);
            var loaded = await db.Transactions.SingleAsync(t => t.Id == expense.Id, Ct);
            loaded.TagIds.Should().BeEquivalentTo([trip.Id, work.Id]);

            loaded.ReplaceWith(new TransactionFactory(loadedBook).WithTags(
                new TransactionFactory(loadedBook).Expense(new DateOnly(2026, 1, 5), cash.Id, food.Id, -100m), [trip.Id]));
            await db.SaveChangesAsync(Ct);
        }

        await using var readDb = createContext();
        (await readDb.Transactions.SingleAsync(t => t.Id == expense.Id, Ct)).TagIds.Should().Equal(trip.Id);
    }
}
