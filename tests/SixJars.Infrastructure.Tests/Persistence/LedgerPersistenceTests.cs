using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using SixJars.Domain.Transactions;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Infrastructure.Tests.Persistence;

public class LedgerPersistenceTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Book_round_trips_with_accounts_funds_and_categories()
    {
        var book = new Book("我的帳本", new DateOnly(2025, 12, 30));
        var foreignCash = book.AddAccount("外幣現鈔", AccountType.Cash, 10000m, countsAsAvailableCash: false);
        book.AddAccount("國泰Combo卡", AccountType.CreditCard, -22668m);
        book.AddPlanningFund("財務自由帳戶", 9874.3m);
        var fixedMain = book.AddExpenseCategory("固定支出", ExpenseNature.Fixed);
        book.AddSubCategory(fixedMain.Id, "保險費");
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        await using (var db = createContext())
        {
            db.Books.Add(book);
            await db.SaveChangesAsync(Ct);
        }

        await using var readDb = createContext();
        var loaded = await readDb.Books.SingleAsync(b => b.Id == book.Id, Ct);

        loaded.OpeningDate.Should().Be(new DateOnly(2025, 12, 30));
        loaded.Accounts.Should().BeEquivalentTo(book.Accounts);
        loaded.GetAccount(foreignCash.Id).CountsAsAvailableCash.Should().BeFalse();
        loaded.PlanningFunds.Single().OpeningBalance.Should().Be(9874.3m);
        loaded.FindCategory("固定支出", "保險費")!.Nature.Should().Be(ExpenseNature.Fixed);
    }

    [Fact]
    public async Task Transaction_round_trips_with_postings_and_budget_month()
    {
        var book = new Book("我的帳本", new DateOnly(2025, 12, 30));
        var bank = book.AddAccount("華南銀行", AccountType.Bank, 11637m);
        var loan = book.AddAccount("房屋貸款", AccountType.Loan, -2658876m);
        var loanExpense = book.AddExpenseCategory("貸款支出", ExpenseNature.Loan);
        var salary = book.AddIncomeCategory("工作薪資1");
        var factory = new TransactionFactory(book);
        var payment = factory.LoanPayment(new DateOnly(2026, 1, 9), bank.Id, loan.Id, 32503m, 28085m, loanExpense.Id, "房貸");
        var income = factory.Income(new DateOnly(2026, 1, 30), bank.Id, salary.Id, 84223m, budgetMonth: new BudgetMonth(2026, 2));
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        await using (var db = createContext())
        {
            db.Books.Add(book);
            db.Transactions.AddRange(payment, income);
            await db.SaveChangesAsync(Ct);
        }

        await using var readDb = createContext();
        var loaded = await readDb.Transactions.Where(t => t.BookId == book.Id).ToListAsync(Ct);

        var loadedPayment = loaded.Single(t => t.Id == payment.Id);
        loadedPayment.Postings.Should().Equal(payment.Postings);
        loadedPayment.LoanPrincipal.Should().Be(28085m);
        loadedPayment.Note.Should().Be("房貸");
        var loadedIncome = loaded.Single(t => t.Id == income.Id);
        loadedIncome.BudgetMonth.Should().Be(new BudgetMonth(2026, 2));
        loadedIncome.Date.Should().Be(new DateOnly(2026, 1, 30));
    }

    [Fact]
    public async Task Planned_expense_round_trips_paid_link()
    {
        var book = new Book("我的帳本", new DateOnly(2025, 12, 30));
        var card = book.AddAccount("國泰Combo卡", AccountType.CreditCard, -22668m);
        var fixedMain = book.AddExpenseCategory("固定支出", ExpenseNature.Fixed);
        var insurance = book.AddSubCategory(fixedMain.Id, "保險費");
        var tx = new TransactionFactory(book).Expense(new DateOnly(2026, 1, 21), card.Id, insurance.Id, -1921m);
        var planned = PlannedExpense.Create(book, new BudgetMonth(2026, 1), insurance.Id, card.Id, -1921m);
        planned.MarkPaid(tx);
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        await using (var db = createContext())
        {
            db.Books.Add(book);
            db.Transactions.Add(tx);
            db.PlannedExpenses.Add(planned);
            await db.SaveChangesAsync(Ct);
        }

        await using var readDb = createContext();
        var loaded = await readDb.PlannedExpenses.SingleAsync(p => p.Id == planned.Id, Ct);

        loaded.PaidTransactionId.Should().Be(tx.Id);
        loaded.BudgetMonth.Should().Be(new BudgetMonth(2026, 1));
        loaded.AccountId.Should().Be(card.Id);
    }
}
