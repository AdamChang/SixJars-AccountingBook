using FluentAssertions;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Transactions;
using SixJars.Infrastructure.Ledger;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Infrastructure.Tests.Ledger;

/// <summary>預算 actual 的來源（P4 L plan D3）：某歸屬月份各分類的支出交易加總，包含電子錢包的消費（ADR 0008）。</summary>
public class ExpenseTotalsByCategoryTests(PostgresFixture postgres)
{
    private static readonly BudgetMonth February = BudgetMonth.FromKey(202602);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sums_expense_transactions_of_the_budget_month_by_category()
    {
        var book = new Book("測試帳本", new DateOnly(2025, 12, 30));
        var cash = book.AddAccount("現金", AccountType.Cash).Id;
        var bank = book.AddAccount("銀行", AccountType.Bank).Id;
        var wallet = book.AddAccount("悠遊卡", AccountType.EWallet).Id;
        var loan = book.AddAccount("房貸", AccountType.Loan).Id;
        var food = book.AddExpenseCategory("飲食", ExpenseNature.Floating).Id;
        var lunch = book.AddSubCategory(food, "午餐").Id;
        var salary = book.AddIncomeCategory("薪資").Id;
        var factory = new TransactionFactory(book);
        var deleted = factory.Expense(new DateOnly(2026, 2, 9), cash, lunch, -999m);
        deleted.Delete(DateTimeOffset.UtcNow);
        Transaction[] transactions =
        [
            factory.Expense(new DateOnly(2026, 2, 10), cash, lunch, -100m),
            factory.Expense(new DateOnly(2026, 2, 11), wallet, lunch, -45m),   // 電子錢包的消費要算
            factory.Expense(new DateOnly(2026, 2, 12), cash, food, -30m),
            factory.Expense(new DateOnly(2026, 2, 13), cash, lunch, 20m),      // 退款
            factory.Expense(new DateOnly(2026, 1, 31), cash, lunch, -7m, budgetMonth: February),
            factory.Expense(new DateOnly(2026, 2, 28), cash, lunch, -500m, budgetMonth: BudgetMonth.FromKey(202603)),
            factory.Income(new DateOnly(2026, 2, 5), bank, salary, 50000m),
            factory.TopUp(new DateOnly(2026, 2, 5), wallet, cash, 300m),
            // 利息分類是浮動的「飲食」也不算：D3 只算 Expense（附錄 Q-A）
            factory.LoanPayment(new DateOnly(2026, 2, 6), bank, loan, 2000m, 1500m, interestCategoryId: food),
            deleted,
        ];
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        await using (var db = createContext())
        {
            db.Books.Add(book);
            db.Transactions.AddRange(transactions);
            await db.SaveChangesAsync(Ct);
        }

        await using var readDb = createContext();
        var totals = await new SqlLedgerSummaryQuery(readDb).ExpenseTotalsByCategoryAsync(book.Id, February, Ct);

        totals.Should().BeEquivalentTo(new Dictionary<CategoryId, decimal> { [lunch] = -132m, [food] = -30m });
    }

    [Fact]
    public async Task Only_the_requested_book_is_summed()
    {
        var mine = new Book("我的", new DateOnly(2025, 12, 30));
        var others = new Book("別人的", new DateOnly(2025, 12, 30));
        var myFood = mine.AddExpenseCategory("飲食", ExpenseNature.Floating).Id;
        var theirFood = others.AddExpenseCategory("飲食", ExpenseNature.Floating).Id;
        var myCash = mine.AddAccount("現金", AccountType.Cash).Id;
        var theirCash = others.AddAccount("現金", AccountType.Cash).Id;
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        await using (var db = createContext())
        {
            db.Books.AddRange(mine, others);
            db.Transactions.AddRange(
                new TransactionFactory(mine).Expense(new DateOnly(2026, 2, 1), myCash, myFood, -10m),
                new TransactionFactory(others).Expense(new DateOnly(2026, 2, 1), theirCash, theirFood, -99m));
            await db.SaveChangesAsync(Ct);
        }

        await using var readDb = createContext();
        var totals = await new SqlLedgerSummaryQuery(readDb).ExpenseTotalsByCategoryAsync(mine.Id, February, Ct);

        totals.Should().BeEquivalentTo(new Dictionary<CategoryId, decimal> { [myFood] = -10m });
    }
}
