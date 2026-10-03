using FluentAssertions;
using SixJars.Application.Ledger;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using SixJars.Domain.Ledger;
using SixJars.Domain.Planning;
using SixJars.Domain.Tests;
using SixJars.Domain.Transactions;
using SixJars.Infrastructure.Ledger;
using SixJars.Infrastructure.Persistence;
using SixJars.Tests.Shared;
using Xunit;

namespace SixJars.Infrastructure.Tests.Ledger;

/// <summary>
/// spec §6 的 oracle 測試：同一份寫進 DB 的資料，SQL 彙總的每個數字都必須等於
/// 「<see cref="LedgerSnapshotLoader"/> 載入快照 → P1 計算器」得到的數字。
/// </summary>
public class SqlLedgerSummaryQueryTests(PostgresFixture postgres)
{
    private static readonly BudgetMonth December = new(2025, 12);
    private static readonly BudgetMonth January = new(2026, 1);
    private static readonly BudgetMonth February = new(2026, 2);
    private static readonly BudgetMonth March = new(2026, 3);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Posting_totals_as_of_date_match_balance_calculator()
    {
        var (sample, ledger, query) = await SeedAsync();
        var balances = new BalanceCalculator(ledger);

        // 1/15 當天有一筆轉帳：截止日必須含當天。
        foreach (var date in new DateOnly[] { new(2026, 1, 15), new(2026, 1, 31), new(2026, 2, 28) })
        {
            var totals = await query.PostingTotalsAsync(sample.Book.Id, new BalanceCutoff.AsOf(date), Ct);

            totals.Keys.Should().BeSubsetOf(sample.Book.Accounts.Select(a => a.Id));
            foreach (var account in sample.Book.Accounts)
            {
                (account.OpeningBalance + totals.GetValueOrDefault(account.Id))
                    .Should().Be(balances.AccountBalanceAsOf(account.Id, date), $"{account.Name} 截至 {date}");
            }
        }
    }

    [Fact]
    public async Task Posting_totals_through_budget_month_match_balance_calculator()
    {
        var (sample, ledger, query) = await SeedAsync();
        var balances = new BalanceCalculator(ledger);
        // 資料本身要能區分兩種截止方式：2/3 的支出歸屬 1 月、1/30 的薪資歸屬 2 月。
        sample.Book.Accounts.Select(a => balances.AccountBalanceThroughBudgetMonth(a.Id, January))
            .Should().NotEqual(sample.Book.Accounts.Select(a => balances.AccountBalanceAsOf(a.Id, new DateOnly(2026, 1, 31))));

        foreach (var month in new[] { January, February })
        {
            var totals = await query.PostingTotalsAsync(sample.Book.Id, new BalanceCutoff.ThroughBudgetMonth(month), Ct);

            totals.Keys.Should().BeSubsetOf(sample.Book.Accounts.Select(a => a.Id));
            foreach (var account in sample.Book.Accounts)
            {
                (account.OpeningBalance + totals.GetValueOrDefault(account.Id))
                    .Should().Be(balances.AccountBalanceThroughBudgetMonth(account.Id, month), $"{account.Name} 截至歸屬月份 {month}");
            }
        }
    }

    [Fact]
    public async Task Fund_totals_match_balance_calculator()
    {
        var (sample, ledger, query) = await SeedAsync();
        var balances = new BalanceCalculator(ledger);
        // 資料本身要能區分兩種截止方式：2/2 的資金回流歸屬 1 月。
        balances.FundBalanceThroughBudgetMonth(sample.FreedomFund.Id, January)
            .Should().NotBe(balances.FundBalanceAsOf(sample.FreedomFund.Id, new DateOnly(2026, 1, 31)));

        foreach (var date in new DateOnly[] { new(2026, 1, 5), new(2026, 1, 31), new(2026, 2, 28) })
        {
            var totals = await query.FundDeltaTotalsAsync(sample.Book.Id, new BalanceCutoff.AsOf(date), Ct);

            totals.Keys.Should().BeSubsetOf(sample.Book.PlanningFunds.Select(f => f.Id));
            foreach (var fund in sample.Book.PlanningFunds)
            {
                (fund.OpeningBalance + totals.GetValueOrDefault(fund.Id))
                    .Should().Be(balances.FundBalanceAsOf(fund.Id, date), $"{fund.Name} 截至 {date}");
            }
        }

        foreach (var month in new[] { January, February })
        {
            var totals = await query.FundDeltaTotalsAsync(sample.Book.Id, new BalanceCutoff.ThroughBudgetMonth(month), Ct);

            totals.Keys.Should().BeSubsetOf(sample.Book.PlanningFunds.Select(f => f.Id));
            foreach (var fund in sample.Book.PlanningFunds)
            {
                (fund.OpeningBalance + totals.GetValueOrDefault(fund.Id))
                    .Should().Be(balances.FundBalanceThroughBudgetMonth(fund.Id, month), $"{fund.Name} 截至歸屬月份 {month}");
            }
        }
    }

    [Fact]
    public async Task Monthly_disposable_matches_calculator()
    {
        var (sample, ledger, query) = await SeedAsync();
        var calculator = new DisposableBalanceCalculator(ledger);

        var monthly = await query.MonthlyDisposableAsync(sample.Book, January, March, Ct);

        // 範圍外（2025-12、2026-04）也有交易，不可出現；範圍內沒有資料的 3 月要補 0。
        monthly.Keys.Should().BeEquivalentTo(new[] { January, February, March });
        monthly[January].Should().Be(calculator.Monthly(January));
        monthly[February].Should().Be(calculator.Monthly(February));
        monthly[March].Should().Be(0m).And.Be(calculator.Monthly(March));
    }

    [Fact]
    public async Task Totals_only_include_the_requested_book()
    {
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        var mine = new Scenario();
        var other = new Scenario();
        await using (var db = createContext())
        {
            mine.AddTo(db);
            other.AddTo(db);
            await db.SaveChangesAsync(Ct);
        }

        await using var readDb = createContext();
        var ledger = await new LedgerSnapshotLoader(readDb).LoadAsync(mine.Sample.Book.Id, Ct);
        var query = new SqlLedgerSummaryQuery(readDb);
        var book = mine.Sample.Book;
        var balances = new BalanceCalculator(ledger);
        var disposable = new DisposableBalanceCalculator(ledger);

        var byDate = await query.PostingTotalsAsync(book.Id, new BalanceCutoff.AsOf(new DateOnly(2026, 2, 28)), Ct);
        var byMonth = await query.PostingTotalsAsync(book.Id, new BalanceCutoff.ThroughBudgetMonth(February), Ct);
        var fundsByDate = await query.FundDeltaTotalsAsync(book.Id, new BalanceCutoff.AsOf(new DateOnly(2026, 2, 28)), Ct);
        var fundsByMonth = await query.FundDeltaTotalsAsync(book.Id, new BalanceCutoff.ThroughBudgetMonth(February), Ct);
        var monthly = await query.MonthlyDisposableAsync(book, January, February, Ct);

        byDate.Keys.Should().BeSubsetOf(book.Accounts.Select(a => a.Id));
        byMonth.Keys.Should().BeSubsetOf(book.Accounts.Select(a => a.Id));
        fundsByDate.Keys.Should().BeSubsetOf(book.PlanningFunds.Select(f => f.Id));
        fundsByMonth.Keys.Should().BeSubsetOf(book.PlanningFunds.Select(f => f.Id));
        foreach (var account in book.Accounts)
        {
            (account.OpeningBalance + byDate.GetValueOrDefault(account.Id))
                .Should().Be(balances.AccountBalanceAsOf(account.Id, new DateOnly(2026, 2, 28)), account.Name);
            (account.OpeningBalance + byMonth.GetValueOrDefault(account.Id))
                .Should().Be(balances.AccountBalanceThroughBudgetMonth(account.Id, February), account.Name);
        }

        foreach (var fund in book.PlanningFunds)
        {
            (fund.OpeningBalance + fundsByDate.GetValueOrDefault(fund.Id))
                .Should().Be(balances.FundBalanceAsOf(fund.Id, new DateOnly(2026, 2, 28)), fund.Name);
            (fund.OpeningBalance + fundsByMonth.GetValueOrDefault(fund.Id))
                .Should().Be(balances.FundBalanceThroughBudgetMonth(fund.Id, February), fund.Name);
        }

        monthly[January].Should().Be(disposable.Monthly(January));
        monthly[February].Should().Be(disposable.Monthly(February));
    }

    /// <summary>寫入一本帳後回傳：帳本、oracle 用的快照（從 DB 載入）、SQL 查詢。</summary>
    private async Task<(SampleBook Sample, LedgerSnapshot Ledger, SqlLedgerSummaryQuery Query)> SeedAsync()
    {
        var createContext = await postgres.CreateDatabaseAsync(Ct);
        var scenario = new Scenario();
        await using (var db = createContext())
        {
            scenario.AddTo(db);
            await db.SaveChangesAsync(Ct);
        }

        // 讀取用的 context 要給回傳的查詢繼續使用，所以不在這裡 dispose；每個測試一個獨立資料庫，不會互相影響。
        var readDb = createContext();
        var ledger = await new LedgerSnapshotLoader(readDb).LoadAsync(scenario.Sample.Book.Id, Ct);
        ledger.Transactions.Select(t => t.Kind).Distinct().Should().HaveCount(Enum.GetValues<TransactionKind>().Length, "必須涵蓋全部交易類型");
        return (scenario.Sample, ledger, new SqlLedgerSummaryQuery(readDb));
    }

    /// <summary>
    /// 涵蓋全部 12 種交易類型與 spec 列出的邊界情況。每一筆的目的寫在旁邊；改資料時要確認變異測試仍然抓得到。
    /// </summary>
    private sealed class Scenario
    {
        public Scenario()
        {
            var s = Sample;
            var f = s.Factory;
            var emergency = s.Book.AddPlanningFund("緊急預備金");
            Transactions =
            [
                // 範圍外：2025-12，測試月可用餘額的 from 邊界。
                f.Income(new DateOnly(2025, 12, 31), s.Bank.Id, s.Salary.Id, 1234m),
                f.Income(new DateOnly(2026, 1, 5), s.Bank.Id, s.Salary.Id, 50000m),
                // 有轉出帳戶的入新資金；1/5 當天，資金截止日測試含當天。
                f.FundAllocation(new DateOnly(2026, 1, 5), s.FreedomFund.Id, s.Investment.Id, s.Bank.Id, 5000m),
                // 同帳戶圈存：沒有分錄，只動財務規劃帳戶。
                f.FundAllocation(new DateOnly(2026, 1, 6), s.FreedomFund.Id, s.Bank.Id, null, 2000m),
                f.Expense(new DateOnly(2026, 1, 8), s.Cash.Id, s.Food.Id, -120m),
                // 電子錢包的消費：不扣月可用餘額（Q5），但會動電子錢包的餘額。
                f.Expense(new DateOnly(2026, 1, 10), s.Wallet.Id, s.Food.Id, -45m),
                f.Expense(new DateOnly(2026, 1, 12), s.Card.Id, s.Food.Id, -800m),
                // 1/15 當天：日期截止測試含當天。
                f.Transfer(new DateOnly(2026, 1, 15), s.Bank.Id, s.OtherBank.Id, 3000m),
                f.Withdrawal(new DateOnly(2026, 1, 16), s.Bank.Id, s.Cash.Id, 2000m),
                f.CashDeposit(new DateOnly(2026, 1, 18), s.Bank.Id, s.Cash.Id, 500m),
                f.TopUp(new DateOnly(2026, 1, 20), s.Wallet.Id, s.Cash.Id, 300m),
                f.CardPayment(new DateOnly(2026, 1, 25), s.Bank.Id, s.Card.Id, 22668m),
                f.LoanDisbursement(new DateOnly(2026, 1, 26), s.OtherBank.Id, s.Loan.Id, 100000m),
                // 本金少於總額：利息 6000 不進任何帳戶，但月可用餘額扣總額。
                f.LoanPayment(new DateOnly(2026, 1, 28), s.Bank.Id, s.Loan.Id, 15000m, 9000m, s.Mortgage.Id),
                // 跨月歸屬：1/30 領薪，歸屬 2 月。
                f.Income(new DateOnly(2026, 1, 30), s.Bank.Id, s.Salary.Id, 60000m, budgetMonth: February),
                // 日期在 1/31 之後、歸屬 1 月：依日期截止不含、依歸屬月份截止要含。
                f.FundReturn(new DateOnly(2026, 2, 2), s.FreedomFund.Id, s.Investment.Id, 80m, budgetMonth: January),
                f.Expense(new DateOnly(2026, 2, 3), s.Card.Id, s.Food.Id, -650m, budgetMonth: January),
                f.TopUp(new DateOnly(2026, 2, 5), s.Wallet.Id, s.Card.Id, 500m),
                f.FundWithdrawal(new DateOnly(2026, 2, 10), s.FreedomFund.Id, s.Investment.Id, 1500m, s.Insurance.Id),
                f.FundAllocation(new DateOnly(2026, 2, 12), emergency.Id, s.Cash.Id, null, 700m),
                f.FundWithdrawal(new DateOnly(2026, 2, 13), emergency.Id, s.Cash.Id, 200m),
                f.Expense(new DateOnly(2026, 2, 14), s.Bank.Id, s.Phone.Id, -599m),
                f.Expense(new DateOnly(2026, 2, 15), s.Wallet.Id, s.Food.Id, -60m),
                // 範圍外：2026-04，測試月可用餘額的 to 邊界。
                f.Expense(new DateOnly(2026, 4, 2), s.Cash.Id, s.Food.Id, -50m),
            ];

            var paidPhone = PlannedExpense.Create(s.Book, February, s.Phone.Id, s.Bank.Id, -599m);
            paidPhone.MarkPaid(Transactions.Single(t => t.Kind == TransactionKind.Expense && t.CategoryId == s.Phone.Id));
            PlannedExpenses =
            [
                PlannedExpense.Create(s.Book, January, s.Insurance.Id, s.Bank.Id, -1200m),
                paidPhone,
                PlannedExpense.Create(s.Book, February, s.Insurance.Id, null, -3000m),
                PlannedExpense.Create(s.Book, December, s.Insurance.Id, null, -111m),
            ];
        }

        public SampleBook Sample { get; } = new();
        public IReadOnlyList<Transaction> Transactions { get; }
        public IReadOnlyList<PlannedExpense> PlannedExpenses { get; }

        public void AddTo(SixJarsDbContext db)
        {
            db.Books.Add(Sample.Book);
            db.Transactions.AddRange(Transactions);
            db.PlannedExpenses.AddRange(PlannedExpenses);
        }
    }
}
