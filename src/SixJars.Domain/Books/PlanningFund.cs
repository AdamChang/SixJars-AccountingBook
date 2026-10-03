using SixJars.Domain.Common;

namespace SixJars.Domain.Books;

/// <summary>財務規劃帳戶：指定用途的虛擬信封，不是帳戶（ADR 0003）。</summary>
public sealed class PlanningFund
{
    internal PlanningFund(PlanningFundId id, string name, decimal openingBalance)
    {
        Id = id;
        Name = name;
        OpeningBalance = openingBalance;
    }

    public PlanningFundId Id { get; private set; }
    public string Name { get; private set; }
    public decimal OpeningBalance { get; private set; }
}
