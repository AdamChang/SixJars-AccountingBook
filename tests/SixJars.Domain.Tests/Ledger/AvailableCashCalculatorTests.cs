using FluentAssertions;
using SixJars.Domain.Common;
using SixJars.Domain.Ledger;
using Xunit;

namespace SixJars.Domain.Tests.Ledger;

public class AvailableCashCalculatorTests
{
    private static readonly BudgetMonth January = new(2026, 1);
    private readonly SampleBook _s = new();

    [Fact]
    public void Counts_only_cash_accounts_flagged_as_available()
    {
        var withdrawal = _s.Factory.Withdrawal(new DateOnly(2026, 1, 6), _s.Bank.Id, _s.Cash.Id, 3000m);
        var calculator = new AvailableCashCalculator(new LedgerSnapshot(_s.Book, [withdrawal], []));

        calculator.ThroughBudgetMonth(January, includeEWallets: false).Should().Be(5071m);
    }

    [Fact]
    public void Optionally_includes_ewallet_balances()
    {
        var calculator = new AvailableCashCalculator(new LedgerSnapshot(_s.Book, [], []));

        calculator.ThroughBudgetMonth(January, includeEWallets: true).Should().Be(2071m + 152m);
        calculator.AsOf(new DateOnly(2026, 1, 1), includeEWallets: false).Should().Be(2071m);
    }
}
