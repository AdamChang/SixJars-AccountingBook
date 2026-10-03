using SixJars.Domain.Common;

namespace SixJars.Application.Ledger;

/// <summary>餘額截止方式：依日期（資產負債表）或依歸屬月份（月報表與驗收；P1 spec §4.4）。</summary>
public abstract record BalanceCutoff
{
    private BalanceCutoff()
    {
    }

    /// <summary>交易日期在 <paramref name="Date"/> 當天或之前（含）。</summary>
    public sealed record AsOf(DateOnly Date) : BalanceCutoff;

    /// <summary>歸屬月份在 <paramref name="Month"/> 或之前（含），不論交易日期。</summary>
    public sealed record ThroughBudgetMonth(BudgetMonth Month) : BalanceCutoff;
}
