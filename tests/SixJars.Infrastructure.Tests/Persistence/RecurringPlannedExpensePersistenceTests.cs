using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Infrastructure.Tests.Persistence;

public class RecurringPlannedExpensePersistenceTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly BudgetMonth April = BudgetMonth.FromKey(202604);

    [Fact]
    public async Task Recurring_item_and_source_round_trip()
    {
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        var (book, insurance) = await SeedAsync(createContext);
        var planned = PlannedExpense.Create(book, April, insurance.CategoryId, null, -3000m, null, sourceId: insurance.Id);
        await using (var db = createContext())
        {
            db.PlannedExpenses.Add(planned);
            await db.SaveChangesAsync(Ct);
        }

        await using var readDb = createContext();
        var item = await readDb.RecurringPlannedExpenses.SingleAsync(Ct);
        item.Frequency.Should().Be(RecurrenceFrequency.Yearly);
        item.Months.Should().Equal(1, 7);
        (item.StartMonth, item.EndMonth).Should().Be((BudgetMonth.FromKey(202601), (BudgetMonth?)null));
        (await readDb.PlannedExpenses.SingleAsync(Ct)).SourceId.Should().Be(insurance.Id);
    }

    [Fact]
    public async Task Same_source_and_month_is_rejected_even_when_the_first_is_soft_deleted()
    {
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        var (book, insurance) = await SeedAsync(createContext);
        var first = PlannedExpense.Create(book, April, insurance.CategoryId, null, -3000m, null, sourceId: insurance.Id);
        first.Delete(DateTimeOffset.UtcNow);
        await using (var db = createContext())
        {
            db.PlannedExpenses.Add(first);
            await db.SaveChangesAsync(Ct);
        }

        await using var again = createContext();
        again.PlannedExpenses.Add(PlannedExpense.Create(book, April, insurance.CategoryId, null, -3000m, null, sourceId: insurance.Id));
        var act = () => again.SaveChangesAsync(Ct);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Planned_expenses_without_source_are_not_constrained()
    {
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        var (book, insurance) = await SeedAsync(createContext);
        await using var db = createContext();
        db.PlannedExpenses.Add(PlannedExpense.Create(book, April, insurance.CategoryId, null, -1m, null));
        db.PlannedExpenses.Add(PlannedExpense.Create(book, April, insurance.CategoryId, null, -2m, null));

        var act = () => db.SaveChangesAsync(Ct);

        await act.Should().NotThrowAsync();
    }

    private async Task<(Book Book, RecurringPlannedExpense Insurance)> SeedAsync(Func<SixJarsDbContext> createContext)
    {
        var book = new Book("測試帳本", new DateOnly(2025, 12, 30));
        var fixedExpense = book.AddExpenseCategory("固定支出", ExpenseNature.Fixed);
        var insurance = RecurringPlannedExpense.Create(
            book, fixedExpense.Id, null, -3000m, "保險", RecurrenceFrequency.Yearly, [7, 1], BudgetMonth.FromKey(202601), null);
        await using var db = createContext();
        db.Books.Add(book);
        db.RecurringPlannedExpenses.Add(insurance);
        await db.SaveChangesAsync(Ct);
        return (book, insurance);
    }
}
