using FluentAssertions;
using SixJars.Domain.Common;
using SixJars.Domain.Ledger;
using Xunit;

namespace SixJars.Domain.Tests.Ledger;

public class BalanceCalculatorTests
{
    private static readonly BudgetMonth January = new(2026, 1);
    private readonly SampleBook _s = new();

    [Fact]
    public void Balance_is_opening_plus_postings_up_to_date()
    {
        var income = _s.Factory.Income(new DateOnly(2026, 1, 5), _s.Bank.Id, _s.Salary.Id, 1000m);
        var expense = _s.Factory.Expense(new DateOnly(2026, 1, 20), _s.Bank.Id, _s.Food.Id, -200m);
        var calculator = new BalanceCalculator(new LedgerSnapshot(_s.Book, [income, expense], []));

        calculator.AccountBalanceAsOf(_s.Bank.Id, new DateOnly(2026, 1, 10)).Should().Be(22610m);
        calculator.AccountBalanceAsOf(_s.Bank.Id, new DateOnly(2026, 1, 31)).Should().Be(22410m);
    }

    [Fact]
    public void Through_budget_month_uses_budget_month_not_date()
    {
        var februarySalary = _s.Factory.Income(new DateOnly(2026, 1, 30), _s.Bank.Id, _s.Salary.Id, 84223m, budgetMonth: new BudgetMonth(2026, 2));
        var calculator = new BalanceCalculator(new LedgerSnapshot(_s.Book, [februarySalary], []));

        calculator.AccountBalanceAsOf(_s.Bank.Id, new DateOnly(2026, 1, 31)).Should().Be(105833m);
        calculator.AccountBalanceThroughBudgetMonth(_s.Bank.Id, January).Should().Be(21610m);
    }

    [Fact]
    public void Card_balance_is_negative_outstanding()
    {
        var spend = _s.Factory.Expense(new DateOnly(2026, 1, 5), _s.Card.Id, _s.Food.Id, -27770m);
        var payment = _s.Factory.CardPayment(new DateOnly(2026, 1, 6), _s.Bank.Id, _s.Card.Id, 18197m);
        var calculator = new BalanceCalculator(new LedgerSnapshot(_s.Book, [spend, payment], []));

        calculator.AccountBalanceThroughBudgetMonth(_s.Card.Id, January).Should().Be(-32241m);
    }

    [Fact]
    public void Fund_balance_tracks_allocation_withdrawal_and_return()
    {
        var date = new DateOnly(2026, 1, 11);
        var allocation = _s.Factory.FundAllocation(date, _s.FreedomFund.Id, _s.Investment.Id, _s.Bank.Id, 8000m);
        var withdrawal = _s.Factory.FundWithdrawal(date, _s.FreedomFund.Id, _s.Investment.Id, 15371m);
        var fundReturn = _s.Factory.FundReturn(date, _s.FreedomFund.Id, _s.Investment.Id, 9857m);
        var calculator = new BalanceCalculator(new LedgerSnapshot(_s.Book, [allocation, withdrawal, fundReturn], []));

        calculator.FundBalanceThroughBudgetMonth(_s.FreedomFund.Id, January).Should().Be(12360.3m);
        calculator.FundBalanceAsOf(_s.FreedomFund.Id, new DateOnly(2026, 1, 10)).Should().Be(9874.3m);
    }
}
