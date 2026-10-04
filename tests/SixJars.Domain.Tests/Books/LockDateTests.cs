using FluentAssertions;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using Xunit;

namespace SixJars.Domain.Tests.Books;

/// <summary>鎖帳日（CONTEXT.md）：此日（含）以前的資料不可異動；預定支出以歸屬月份的最後一天判斷（spec §3.1）。</summary>
public class LockDateTests
{
    private readonly Book _book = new("測試帳本", new DateOnly(2025, 12, 31));

    [Theory]
    [InlineData("2026-01-30", true)]
    [InlineData("2026-01-31", true)]
    [InlineData("2026-02-01", false)]
    public void EnsureUnlocked_rejects_dates_on_or_before_lock_date(string date, bool locked)
    {
        _book.SetLockDate(new DateOnly(2026, 1, 31));

        var act = () => _book.EnsureUnlocked(DateOnly.Parse(date));

        if (locked)
        {
            act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainException.LockedCode);
        }
        else
        {
            act.Should().NotThrow();
        }
    }

    [Fact]
    public void Month_ending_on_lock_date_is_locked()
    {
        _book.SetLockDate(new DateOnly(2026, 1, 31));

        var act = () => _book.EnsureUnlocked(new BudgetMonth(2026, 1));

        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainException.LockedCode);
    }

    [Fact]
    public void Month_after_lock_date_is_open()
    {
        // 1/31 還沒鎖，所以 2026-01 整個月仍然開放。
        _book.SetLockDate(new DateOnly(2026, 1, 30));

        var act = () => _book.EnsureUnlocked(new BudgetMonth(2026, 1));

        act.Should().NotThrow();
    }

    [Fact]
    public void Clearing_lock_date_unlocks_everything()
    {
        _book.SetLockDate(new DateOnly(2026, 1, 31));

        _book.SetLockDate(null);

        _book.LockDate.Should().BeNull();
        _book.Invoking(b => b.EnsureUnlocked(new DateOnly(2000, 1, 1))).Should().NotThrow();
        _book.Invoking(b => b.EnsureUnlocked(new BudgetMonth(2000, 1))).Should().NotThrow();
    }
}
