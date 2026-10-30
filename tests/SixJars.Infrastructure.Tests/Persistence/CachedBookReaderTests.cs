using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SixJars.Domain.Books;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Infrastructure.Tests.Persistence;

/// <summary>
/// 快取跨 request 共用（同一個 <see cref="IMemoryCache"/>），每次讀取各用一個新的 DbContext，模擬不同 request；
/// 寫入用另一個 DbContext，模擬另一個 Cloud Run instance 或 CLI。
/// </summary>
public class CachedBookReaderTests(PostgresFixture postgres)
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    [Fact]
    public async Task Unchanged_book_is_served_from_the_cache()
    {
        var ct = TestContext.Current.CancellationToken;
        var (createContext, book) = await SeedAsync(ct);

        var first = await ReadAsync(createContext, book, ct);
        var second = await ReadAsync(createContext, book, ct);

        second.Should().BeSameAs(first);
    }

    [Fact]
    public async Task Renaming_an_account_elsewhere_invalidates_the_cache()
    {
        var ct = TestContext.Current.CancellationToken;
        var (createContext, book) = await SeedAsync(ct);
        var cash = book.FindAccount("現金")!.Id;
        await ReadAsync(createContext, book, ct);

        await ModifyAsync(createContext, book, b => b.RenameAccount(cash, "錢包"), ct);

        (await ReadAsync(createContext, book, ct)).GetAccount(cash).Name.Should().Be("錢包");
    }

    [Fact]
    public async Task Adding_a_tag_elsewhere_invalidates_the_cache()
    {
        var ct = TestContext.Current.CancellationToken;
        var (createContext, book) = await SeedAsync(ct);
        await ReadAsync(createContext, book, ct);

        await ModifyAsync(createContext, book, b => b.AddTag("旅行", TagColor.Red), ct);

        (await ReadAsync(createContext, book, ct)).Tags.Select(t => t.Name).Should().Equal("旅行");
    }

    [Fact]
    public async Task Changing_the_lock_date_elsewhere_invalidates_the_cache()
    {
        var ct = TestContext.Current.CancellationToken;
        var (createContext, book) = await SeedAsync(ct);
        await ReadAsync(createContext, book, ct);

        await ModifyAsync(createContext, book, b => b.SetLockDate(new DateOnly(2026, 1, 31)), ct);

        (await ReadAsync(createContext, book, ct)).LockDate.Should().Be(new DateOnly(2026, 1, 31));
    }

    [Fact]
    public async Task Deleted_book_is_not_served_from_the_cache()
    {
        var ct = TestContext.Current.CancellationToken;
        var (createContext, book) = await SeedAsync(ct);
        await ReadAsync(createContext, book, ct);

        await using (var db = createContext())
        {
            await db.Books.Where(b => b.Id == book.Id).ExecuteDeleteAsync(ct);
        }

        await using var readDb = createContext();
        (await new CachedBookReader(readDb, _cache).FindAsync(book.Id, ct)).Should().BeNull();
    }

    private async Task<(Func<SixJarsDbContext> CreateContext, Book Book)> SeedAsync(CancellationToken ct)
    {
        var book = new Book("我的帳本", new DateOnly(2025, 12, 31));
        book.AddAccount("現金", AccountType.Cash);
        book.AddExpenseCategory("飲食", ExpenseNature.Floating);
        book.AddPlanningFund("財務自由帳戶");
        var createContext = await postgres.CreateDatabaseAsync(ct);
        await using var db = createContext();
        db.Books.Add(book);
        await db.SaveChangesAsync(ct);
        return (createContext, book);
    }

    private async Task<Book> ReadAsync(Func<SixJarsDbContext> createContext, Book book, CancellationToken ct)
    {
        await using var db = createContext();
        return (await new CachedBookReader(db, _cache).FindAsync(book.Id, ct))!;
    }

    private static async Task ModifyAsync(Func<SixJarsDbContext> createContext, Book book, Action<Book> modify, CancellationToken ct)
    {
        await using var db = createContext();
        var tracked = await db.Books.SingleAsync(b => b.Id == book.Id, ct);
        modify(tracked);
        await db.SaveChangesAsync(ct);
    }
}
