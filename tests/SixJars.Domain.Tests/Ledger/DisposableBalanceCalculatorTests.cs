using FluentAssertions;
using SixJars.Domain.Common;
using SixJars.Domain.Ledger;
using SixJars.Domain.Planning;
using SixJars.Domain.Transactions;
using Xunit;

namespace SixJars.Domain.Tests.Ledger;

public class DisposableBalanceCalculatorTests
{
    private static readonly DateOnly Jan5 = new(2026, 1, 5);
    private static readonly BudgetMonth January = new(2026, 1);
    private readonly SampleBook _s = new();

    private decimal MonthlyOf(BudgetMonth month, IReadOnlyList<Transaction> transactions, IReadOnlyList<PlannedExpense>? planned = null) =>
        new DisposableBalanceCalculator(new LedgerSnapshot(_s.Book, transactions, planned ?? [])).Monthly(month);

    [Fact]
    public void Income_minus_expenses_on_any_non_wallet_account() =>
        MonthlyOf(January,
        [
            _s.Factory.Income(Jan5, _s.Bank.Id, _s.Salary.Id, 84290m),
            _s.Factory.Expense(Jan5, _s.Card.Id, _s.Food.Id, -129m),
            _s.Factory.Expense(Jan5, _s.Bank.Id, _s.Food.Id, -194m),
        ]).Should().Be(83967m);

    [Fact]
    public void Wallet_spending_is_excluded_but_top_up_is_deducted() =>
        MonthlyOf(January,
        [
            _s.Factory.TopUp(Jan5, _s.Wallet.Id, _s.Bank.Id, 500m),
            _s.Factory.Expense(Jan5, _s.Wallet.Id, _s.Food.Id, -100m),
        ]).Should().Be(-500m);

    [Fact]
    public void Fund_allocation_deducts_but_withdrawal_and_return_do_not() =>
        MonthlyOf(January,
        [
            _s.Factory.FundAllocation(Jan5, _s.FreedomFund.Id, _s.Investment.Id, _s.Bank.Id, 8000m),
            _s.Factory.FundWithdrawal(Jan5, _s.FreedomFund.Id, _s.Investment.Id, 5000m),
            _s.Factory.FundReturn(Jan5, _s.FreedomFund.Id, _s.Investment.Id, 254m),
        ]).Should().Be(-8000m);

    [Fact]
    public void Loan_payment_deducts_full_amount() =>
        MonthlyOf(January, [_s.Factory.LoanPayment(Jan5, _s.OtherBank.Id, _s.Loan.Id, 32503m, 28085m, _s.Mortgage.Id)])
            .Should().Be(-32503m);

    [Fact]
    public void Transfers_card_payments_and_disbursements_are_neutral() =>
        MonthlyOf(January,
        [
            _s.Factory.Transfer(Jan5, _s.Bank.Id, _s.OtherBank.Id, 1000m),
            _s.Factory.Withdrawal(Jan5, _s.Bank.Id, _s.Cash.Id, 3000m),
            _s.Factory.CashDeposit(Jan5, _s.Bank.Id, _s.Cash.Id, 2000m),
            _s.Factory.CardPayment(Jan5, _s.Bank.Id, _s.Card.Id, 18197m),
            _s.Factory.LoanDisbursement(Jan5, _s.Bank.Id, _s.Loan.Id, 500000m),
        ]).Should().Be(0m);

    [Fact]
    public void Unpaid_plan_occupies_estimate_and_paid_plan_uses_actual()
    {
        var unpaid = PlannedExpense.Create(_s.Book, January, _s.Phone.Id, _s.Bank.Id, -599m);
        var paid = PlannedExpense.Create(_s.Book, January, _s.Insurance.Id, _s.Card.Id, -1921m);
        var actual = _s.Factory.Expense(new DateOnly(2026, 1, 21), _s.Card.Id, _s.Insurance.Id, -1900m);
        paid.MarkPaid(actual);

        MonthlyOf(January, [actual], [unpaid, paid]).Should().Be(-2499m);
    }

    [Fact]
    public void Uses_budget_month_not_date()
    {
        var salary = _s.Factory.Income(new DateOnly(2026, 1, 30), _s.Bank.Id, _s.Salary.Id, 84223m, budgetMonth: new BudgetMonth(2026, 2));

        MonthlyOf(January, [salary]).Should().Be(0m);
        MonthlyOf(new BudgetMonth(2026, 2), [salary]).Should().Be(84223m);
    }

    [Fact]
    public void Year_to_date_sums_months_of_the_year()
    {
        var ledger = new LedgerSnapshot(_s.Book,
        [
            _s.Factory.Income(new DateOnly(2026, 1, 3), _s.Bank.Id, _s.Salary.Id, 100m),
            _s.Factory.Expense(new DateOnly(2026, 2, 3), _s.Bank.Id, _s.Food.Id, -30m),
            _s.Factory.Income(new DateOnly(2026, 3, 3), _s.Bank.Id, _s.Salary.Id, 5m),
        ], []);
        var calculator = new DisposableBalanceCalculator(ledger);

        calculator.YearToDate(new BudgetMonth(2026, 2)).Should().Be(70m);
        calculator.YearToDate(new BudgetMonth(2026, 3)).Should().Be(75m);
    }
}
