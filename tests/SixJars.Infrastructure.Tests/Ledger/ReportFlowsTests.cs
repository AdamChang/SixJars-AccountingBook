using FluentAssertions;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using SixJars.Domain.Reports;
using SixJars.Domain.Transactions;
using SixJars.Infrastructure.Ledger;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Infrastructure.Tests.Ledger;

/// <summary>月報與年報的資料來源（P4 M plan D2）：依歸屬月份 × 類型 × 分類 × 對方帳戶 × 財務規劃帳戶加總。</summary>
public class ReportFlowsTests(PostgresFixture postgres)
{
    private static readonly BudgetMonth January = BudgetMonth.FromKey(202601);
    private static readonly BudgetMonth February = BudgetMonth.FromKey(202602);
    private static readonly BudgetMonth March = BudgetMonth.FromKey(202603);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Flows_are_grouped_by_month_kind_category_counter_and_fund()
    {
        var book = new Book("測試帳本", new DateOnly(2025, 12, 30));
        var cash = book.AddAccount("現金", AccountType.Cash).Id;
        var bank = book.AddAccount("銀行", AccountType.Bank).Id;
        var wallet = book.AddAccount("悠遊卡", AccountType.EWallet).Id;
        var loan = book.AddAccount("房貸", AccountType.Loan).Id;
        var fund = book.AddPlanningFund("旅遊").Id;
        var food = book.AddExpenseCategory("飲食", ExpenseNature.Floating).Id;
        var salary = book.AddIncomeCategory("薪資").Id;
        var factory = new TransactionFactory(book);
        var deleted = factory.Expense(new DateOnly(2026, 2, 9), cash, food, -999m);
        deleted.Delete(DateTimeOffset.UtcNow);
        Transaction[] transactions =
        [
            factory.Expense(new DateOnly(2026, 2, 10), cash, food, -100m),
            factory.Expense(new DateOnly(2026, 2, 11), wallet, food, -45m),                       // 電子錢包的消費要算（ADR 0008）
            factory.Income(new DateOnly(2026, 1, 25), bank, salary, 50000m, budgetMonth: February), // 依歸屬月份，不是日期
            factory.LoanPayment(new DateOnly(2026, 2, 6), bank, loan, 2000m, 1500m),
            factory.LoanPayment(new DateOnly(2026, 2, 20), bank, loan, 1000m, 1000m),
            factory.FundAllocation(new DateOnly(2026, 2, 1), fund, bank, null, 3000m),
            factory.FundWithdrawal(new DateOnly(2026, 2, 2), fund, bank, 500m, food),
            factory.FundReturn(new DateOnly(2026, 2, 3), fund, bank, 20m),
            factory.Transfer(new DateOnly(2026, 2, 4), bank, cash, 700m),                          // 不在報表
            factory.TopUp(new DateOnly(2026, 2, 5), wallet, cash, 300m),                           // 不在報表
            factory.Expense(new DateOnly(2026, 3, 1), cash, food, -1m),                            // 範圍外
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
        var flows = await new SqlLedgerSummaryQuery(readDb).ReportFlowsAsync(book.Id, February, February, Ct);

        flows.Should().BeEquivalentTo(new[]
        {
            new ReportFlow(February, TransactionKind.Expense, food, null, null, -145m),
            new ReportFlow(February, TransactionKind.Income, salary, null, null, 50000m),
            new ReportFlow(February, TransactionKind.LoanPayment, null, loan, null, 3000m, 2500m),
            new ReportFlow(February, TransactionKind.FundAllocation, null, null, fund, 3000m),
            new ReportFlow(February, TransactionKind.FundWithdrawal, food, null, fund, 500m),
            new ReportFlow(February, TransactionKind.FundReturn, null, null, fund, 20m),
        });
    }

    [Fact]
    public async Task Range_is_inclusive_and_only_the_requested_book_is_summed()
    {
        var mine = new Book("我的", new DateOnly(2025, 12, 30));
        var others = new Book("別人的", new DateOnly(2025, 12, 30));
        var myFood = mine.AddExpenseCategory("飲食", ExpenseNature.Floating).Id;
        var theirFood = others.AddExpenseCategory("飲食", ExpenseNature.Floating).Id;
        var myCash = mine.AddAccount("現金", AccountType.Cash).Id;
        var theirCash = others.AddAccount("現金", AccountType.Cash).Id;
        var my = new TransactionFactory(mine);
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        await using (var db = createContext())
        {
            db.Books.AddRange(mine, others);
            db.Transactions.AddRange(
                my.Expense(new DateOnly(2026, 1, 5), myCash, myFood, -1m),
                my.Expense(new DateOnly(2026, 2, 5), myCash, myFood, -2m),
                my.Expense(new DateOnly(2026, 3, 5), myCash, myFood, -3m),
                new TransactionFactory(others).Expense(new DateOnly(2026, 2, 5), theirCash, theirFood, -50m));
            await db.SaveChangesAsync(Ct);
        }

        await using var readDb = createContext();
        var flows = await new SqlLedgerSummaryQuery(readDb).ReportFlowsAsync(mine.Id, January, February, Ct);

        flows.Select(f => (f.Month, f.Amount)).Should().BeEquivalentTo(new[] { (January, -1m), (February, -2m) });
    }

    [Fact]
    public async Task Unpaid_planned_by_category_excludes_paid_deleted_and_other_months()
    {
        var book = new Book("測試帳本", new DateOnly(2025, 12, 30));
        var cash = book.AddAccount("現金", AccountType.Cash).Id;
        var fixedMain = book.AddExpenseCategory("固定支出", ExpenseNature.Fixed).Id;
        var insurance = book.AddSubCategory(fixedMain, "保險費").Id;
        var paidTransaction = new TransactionFactory(book).Expense(new DateOnly(2026, 2, 3), cash, fixedMain, -300m);
        var paid = PlannedExpense.Create(book, February, fixedMain, cash, -300m);
        paid.MarkPaid(paidTransaction);
        var removed = PlannedExpense.Create(book, February, insurance, null, -100m);
        removed.Delete(DateTimeOffset.UtcNow);
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        await using (var db = createContext())
        {
            db.Books.Add(book);
            db.Transactions.Add(paidTransaction);
            db.PlannedExpenses.AddRange(
                PlannedExpense.Create(book, February, insurance, cash, -2000m),
                PlannedExpense.Create(book, February, insurance, null, -500m),
                paid,
                removed,
                PlannedExpense.Create(book, March, insurance, null, -700m));
            await db.SaveChangesAsync(Ct);
        }

        await using var readDb = createContext();
        var unpaid = await new SqlLedgerSummaryQuery(readDb).UnpaidPlannedByCategoryAsync(book.Id, February, Ct);

        unpaid.Should().BeEquivalentTo(new Dictionary<CategoryId, decimal> { [insurance] = -2500m });
    }
}
