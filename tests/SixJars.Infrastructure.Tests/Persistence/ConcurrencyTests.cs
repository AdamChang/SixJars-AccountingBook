using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SixJars.Domain.Books;
using SixJars.Domain.Transactions;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Infrastructure.Tests.Persistence;

public class ConcurrencyTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// spike S2b、S2c 的回歸測試：只改 owned 分錄時，EF 不會更新 owner，也就不會檢查 xmin；
    /// <c>ExpectVersion</c> 必須強制把整筆標為 Modified，衝突才會被發現。
    /// </summary>
    [Fact]
    public async Task Changing_only_postings_still_checks_version()
    {
        var book = new Book("我的帳本", new DateOnly(2025, 12, 30));
        var cash = book.AddAccount("現金", AccountType.Cash, 1000m);
        var lunch = book.AddSubCategory(book.AddExpenseCategory("主食", ExpenseNature.Floating).Id, "午餐");
        var factory = new TransactionFactory(book);
        var original = factory.Expense(new DateOnly(2026, 1, 5), cash.Id, lunch.Id, -120m, "午餐");
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        await using (var seed = createContext())
        {
            seed.Books.Add(book);
            seed.Transactions.Add(original);
            await seed.SaveChangesAsync(Ct);
        }

        await using var db = createContext();
        var transaction = await db.Transactions.SingleAsync(t => t.Id == original.Id, Ct);
        var staleVersion = db.GetVersion(transaction);

        // 另一個人只改了備註，沒有動到分錄：分錄列不變，我們這邊刪除舊分錄時不會因為找不到列而意外擲出衝突。
        await using (var other = createContext())
        {
            await other.Transactions.Where(t => t.Id == original.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Note, "別人改的"), Ct);
        }

        // draft 的欄位與目前完全相同，ReplaceWith 之後只有分錄被整組換掉（刪除舊列、新增新列），交易本身沒有任何欄位改變。
        var draft = factory.Expense(new DateOnly(2026, 1, 5), cash.Id, lunch.Id, -120m, "午餐");
        db.ExpectVersion(transaction, staleVersion);
        transaction.ReplaceWith(draft);

        var save = () => db.SaveChangesAsync(Ct);

        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
