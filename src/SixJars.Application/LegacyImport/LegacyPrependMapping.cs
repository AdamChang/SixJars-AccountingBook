using SixJars.Domain.Common;
using SixJars.Domain.Planning;
using SixJars.Domain.Transactions;

namespace SixJars.Application.LegacyImport;

/// <summary>補匯舊年度時，舊年度對應到既有帳本的結果（spec D3）。既有項目的期初不會被改寫。</summary>
/// <param name="OpeningDate">舊年度推算的期初日（修正後最早日期的前一天）。</param>
/// <param name="AccountOpenings">Excel 有出現的每個帳戶的舊年度期初，含對應時新增的帳戶。</param>
/// <param name="FundOpenings">同上，財務規劃帳戶。</param>
/// <param name="Sources">每筆交易的來源列。</param>
/// <param name="AddedAccounts">只在舊年度出現、對應時加入帳本的帳戶（尚未封存）。</param>
public sealed record LegacyPrependMapping(
    DateOnly OpeningDate,
    IReadOnlyList<Transaction> Transactions,
    IReadOnlyList<PlannedExpense> PlannedExpenses,
    IReadOnlyDictionary<AccountId, decimal> AccountOpenings,
    IReadOnlyDictionary<PlanningFundId, decimal> FundOpenings,
    IReadOnlySet<AccountId> AddedAccounts,
    IReadOnlySet<PlanningFundId> AddedPlanningFunds,
    IReadOnlySet<CategoryId> AddedCategories,
    ImportReport Report,
    IReadOnlyDictionary<TransactionId, LegacySource> Sources);

/// <summary>交易在舊 Excel 的來源：工作表名稱（例如「3月」）與列號。</summary>
public sealed record LegacySource(string Sheet, int Row);
