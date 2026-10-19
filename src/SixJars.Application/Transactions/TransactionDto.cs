using SixJars.Domain.Transactions;

namespace SixJars.Application.Transactions;

/// <summary>交易的 API 輸出：輸入欄位加上 Id、展開後的分錄、利息與樂觀並行版本（修改時帶回）。</summary>
/// <param name="BudgetMonth">歸屬月份（yyyymm）。</param>
/// <param name="TagIds">
/// 依 Guid 值排序（顯示時的設定排序由前端與匯出套用，tags spec D5）；<see cref="From"/> 一律帶出，
/// 預設值 null 只為了讀取 v4 以前的備份。
/// </param>
public sealed record TransactionDto(
    Guid Id,
    TransactionKind Kind,
    DateOnly Date,
    int BudgetMonth,
    decimal Amount,
    Guid AccountId,
    Guid? CounterAccountId,
    Guid? CategoryId,
    Guid? PlanningFundId,
    decimal? LoanPrincipal,
    decimal? LoanInterest,
    string? Note,
    IReadOnlyList<PostingDto> Postings,
    uint Version,
    IReadOnlyList<Guid>? TagIds = null)
{
    public static TransactionDto From(Transaction t, uint version) => new(
        t.Id.Value,
        t.Kind,
        t.Date,
        t.BudgetMonth.Key,
        t.Amount,
        t.AccountId.Value,
        t.CounterAccountId?.Value,
        t.CategoryId?.Value,
        t.PlanningFundId?.Value,
        t.LoanPrincipal,
        t.LoanInterest,
        t.Note,
        [.. t.Postings.Select(p => new PostingDto(p.AccountId.Value, p.Amount))],
        version,
        [.. t.TagIds.Select(id => id.Value).Order()]);

    /// <summary>轉回輸入模型，供備份還原（T42）重建交易。</summary>
    public TransactionInput ToInput() =>
        new(Kind, Date, BudgetMonth, Amount, AccountId, CounterAccountId, CategoryId, PlanningFundId, LoanPrincipal, Note, TagIds);
}

public sealed record PostingDto(Guid AccountId, decimal Amount);
