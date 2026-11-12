using FluentAssertions;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using Xunit;

namespace SixJars.Domain.Tests.Books;

/// <summary>補匯舊年度（spec D6）：期初日只能往前移，期初餘額與期初日一起改寫。</summary>
public class BookPrependHistoryTests
{
    private static readonly DateOnly Opening2026 = new(2025, 12, 31);
    private static readonly DateOnly Opening2025 = new(2024, 12, 31);

    private readonly Book _book = new("測試帳本", Opening2026);
    private readonly Account _bank;
    private readonly Account _card;
    private readonly PlanningFund _fund;

    public BookPrependHistoryTests()
    {
        _bank = _book.AddAccount("國泰世華銀行", AccountType.Bank, 100_000m);
        _card = _book.AddAccount("國泰信用卡", AccountType.CreditCard, -5_000m);
        _fund = _book.AddPlanningFund("緊急預備金", 30_000m);
    }

    [Fact]
    public void Moves_opening_date_back_and_rewrites_given_openings_only()
    {
        _book.PrependHistory(Opening2025,
            new Dictionary<AccountId, decimal> { [_bank.Id] = 80_000m },
            new Dictionary<PlanningFundId, decimal> { [_fund.Id] = 20_000m });

        _book.OpeningDate.Should().Be(Opening2025);
        _bank.OpeningBalance.Should().Be(80_000m);
        _card.OpeningBalance.Should().Be(-5_000m);
        _fund.OpeningBalance.Should().Be(20_000m);
    }

    [Theory]
    [InlineData("2025-12-31")]
    [InlineData("2026-01-01")]
    public void Rejects_opening_date_not_earlier_than_current(string date)
    {
        var act = () => _book.PrependHistory(DateOnly.Parse(date),
            new Dictionary<AccountId, decimal> { [_bank.Id] = 80_000m },
            new Dictionary<PlanningFundId, decimal>());

        act.Should().Throw<DomainException>();
        _book.OpeningDate.Should().Be(Opening2026);
        _bank.OpeningBalance.Should().Be(100_000m);
    }

    [Fact]
    public void Unknown_id_changes_nothing()
    {
        var act = () => _book.PrependHistory(Opening2025,
            new Dictionary<AccountId, decimal> { [_bank.Id] = 80_000m },
            new Dictionary<PlanningFundId, decimal> { [PlanningFundId.New()] = 1m });

        act.Should().Throw<DomainException>();
        _book.OpeningDate.Should().Be(Opening2026);
        _bank.OpeningBalance.Should().Be(100_000m);
    }
}
