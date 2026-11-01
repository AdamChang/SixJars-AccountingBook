using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Transactions;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Infrastructure.Tests.Persistence;

/// <summary>
/// 交易來源（經由、來源識別碼）的持久化。partial unique index 是重複保護的最後一道防線：
/// HasFilter 是原生 SQL 字串，欄位名稱大小寫或引號寫錯 migration 仍會成功、index 卻永遠不生效，所以必須用真的 PostgreSQL 驗證。
/// </summary>
public class TransactionOriginPersistenceTests(PostgresFixture postgres)
{
    private const string Ref = "<a@m>";
    private static readonly DateOnly Day = new(2026, 1, 5);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (Book Book, TransactionFactory Factory, AccountId Cash, CategoryId Lunch) NewBook()
    {
        var book = new Book("我的帳本", new DateOnly(2025, 12, 30));
        var cash = book.AddAccount("現金", AccountType.Cash, 1000m);
        var lunch = book.AddSubCategory(book.AddExpenseCategory("主食", ExpenseNature.Floating).Id, "午餐");
        return (book, new TransactionFactory(book), cash.Id, lunch.Id);
    }

    private static Transaction NewTransaction((Book Book, TransactionFactory Factory, AccountId Cash, CategoryId Lunch) b, string? via, string? externalRef)
    {
        var transaction = b.Factory.Expense(Day, b.Cash, b.Lunch, -120m, "午餐");
        transaction.SetOrigin(via, externalRef);
        return transaction;
    }

    [Fact]
    public async Task Origin_round_trips()
    {
        var b = NewBook();
        var original = NewTransaction(b, "mcp:claude.ai", Ref);
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        await using (var seed = createContext())
        {
            seed.Books.Add(b.Book);
            seed.Transactions.Add(original);
            await seed.SaveChangesAsync(Ct);
        }

        await using var db = createContext();
        var loaded = await db.Transactions.SingleAsync(t => t.Id == original.Id, Ct);

        loaded.CreatedVia.Should().Be("mcp:claude.ai");
        loaded.ExternalRef.Should().Be(Ref);
    }

    [Fact]
    public async Task Same_external_ref_in_same_book_is_rejected()
    {
        var b = NewBook();
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        await using var db = createContext();
        db.Books.Add(b.Book);
        db.Transactions.Add(NewTransaction(b, null, Ref));
        await db.SaveChangesAsync(Ct);

        db.Transactions.Add(NewTransaction(b, null, Ref));
        var save = () => db.SaveChangesAsync(Ct);

        var thrown = await save.Should().ThrowAsync<DbUpdateException>();
        thrown.Which.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be("23505");
    }

    [Fact]
    public async Task Same_external_ref_is_allowed_after_soft_delete()
    {
        var b = NewBook();
        var first = NewTransaction(b, null, Ref);
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        await using var db = createContext();
        db.Books.Add(b.Book);
        db.Transactions.Add(first);
        await db.SaveChangesAsync(Ct);
        first.Delete(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(Ct);

        var second = NewTransaction(b, null, Ref);
        db.Transactions.Add(second);
        await db.SaveChangesAsync(Ct);

        (await db.Transactions.CountAsync(t => t.ExternalRef == Ref, Ct)).Should().Be(1);
    }

    [Fact]
    public async Task Same_external_ref_in_another_book_is_allowed()
    {
        var one = NewBook();
        var two = NewBook();
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        await using var db = createContext();
        db.Books.AddRange(one.Book, two.Book);
        db.Transactions.Add(NewTransaction(one, null, Ref));
        db.Transactions.Add(NewTransaction(two, null, Ref));

        var save = () => db.SaveChangesAsync(Ct);

        await save.Should().NotThrowAsync();
    }
}
