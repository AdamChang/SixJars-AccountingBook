using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Infrastructure.Tests.Persistence;

/// <summary>預算的對應，以及 P4 L plan D1（覆寫值存成 owned collection）依賴的三個 EF 行為；留作回歸測試。</summary>
public class CategoryBudgetPersistenceTests(PostgresFixture postgres)
{
    private static readonly BudgetMonth January = BudgetMonth.FromKey(202601);
    private static readonly BudgetMonth February = BudgetMonth.FromKey(202602);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Budget_with_default_and_overrides_round_trips()
    {
        var (createContext, book, food) = await SeedAsync();
        await using (var db = createContext())
        {
            var budget = CategoryBudget.Create(book, food);
            budget.SetDefault(5000m);
            budget.SetOverride(January, 3000m);
            budget.SetOverride(February, 0m);
            db.CategoryBudgets.Add(budget);
            await db.SaveChangesAsync(Ct);
        }

        await using var readDb = createContext();
        var read = await readDb.CategoryBudgets.SingleAsync(Ct);
        (read.CategoryId, read.BookId, read.DefaultAmount).Should().Be((food, book.Id, (decimal?)5000m));
        read.Overrides.Select(o => (o.Month, o.Amount)).Should().BeEquivalentTo([(January, 3000m), (February, 0m)]);
    }

    /// <summary>假設 (a)：原地改金額（L1）→ owned 列是 Modified，發出 UPDATE。</summary>
    [Fact]
    public async Task Changing_an_override_amount_updates_the_owned_row()
    {
        var (createContext, _, food) = await SeedWithOverrideAsync(January, 3000m);

        await using (var db = createContext())
        {
            var budget = await db.CategoryBudgets.SingleAsync(b => b.CategoryId == food, Ct);
            budget.SetOverride(January, 3500m);
            db.ChangeTracker.DetectChanges();
            db.ChangeTracker.Entries<BudgetOverride>().Should().ContainSingle().Which.State.Should().Be(EntityState.Modified);
            await db.SaveChangesAsync(Ct);
        }

        await using var readDb = createContext();
        (await readDb.CategoryBudgets.SingleAsync(Ct)).Overrides.Should().ContainSingle().Which.Amount.Should().Be(3500m);
    }

    /// <summary>假設 (b)：同一個 DbContext 先移除、再加回同一個月份（新的實例、相同的鍵）。</summary>
    [Fact]
    public async Task Removing_and_re_adding_the_same_month_in_one_context_saves()
    {
        var (createContext, _, food) = await SeedWithOverrideAsync(January, 3000m);

        await using (var db = createContext())
        {
            var budget = await db.CategoryBudgets.SingleAsync(b => b.CategoryId == food, Ct);
            budget.RemoveOverride(January);
            budget.SetOverride(January, 100m);
            await db.SaveChangesAsync(Ct);
        }

        await using var readDb = createContext();
        (await readDb.CategoryBudgets.SingleAsync(Ct)).Overrides.Should().ContainSingle()
            .Which.Should().Match<BudgetOverride>(o => o.Month == January && o.Amount == 100m);
    }

    /// <summary>假設 (c)：刪除整筆（Q1c）連帶刪掉覆寫值，資料表不留孤兒列。</summary>
    [Fact]
    public async Task Removing_the_budget_deletes_its_overrides()
    {
        var (createContext, _, food) = await SeedWithOverrideAsync(January, 3000m);

        await using (var db = createContext())
        {
            db.CategoryBudgets.Remove(await db.CategoryBudgets.SingleAsync(b => b.CategoryId == food, Ct));
            await db.SaveChangesAsync(Ct);
        }

        await using var readDb = createContext();
        (await readDb.CategoryBudgets.CountAsync(Ct)).Should().Be(0);
        (await readDb.Database.SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM \"CategoryBudgetOverrides\"").SingleAsync(Ct))
            .Should().Be(0);
    }

    private async Task<(Func<SixJarsDbContext> CreateContext, Book Book, CategoryId Food)> SeedAsync()
    {
        var book = new Book("測試帳本", new DateOnly(2025, 12, 30));
        var food = book.AddExpenseCategory("飲食", ExpenseNature.Floating).Id;
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        await using var db = createContext();
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return (createContext, book, food);
    }

    private async Task<(Func<SixJarsDbContext> CreateContext, Book Book, CategoryId Food)> SeedWithOverrideAsync(BudgetMonth month, decimal amount)
    {
        var (createContext, book, food) = await SeedAsync();
        await using var db = createContext();
        var budget = CategoryBudget.Create(book, food);
        budget.SetOverride(month, amount);
        db.CategoryBudgets.Add(budget);
        await db.SaveChangesAsync(Ct);
        return (createContext, book, food);
    }
}
