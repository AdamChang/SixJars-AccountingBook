using SixJars.Domain.Common;

namespace SixJars.Domain.Transactions;

/// <summary>交易對單一帳戶的帶號金額變動（資產觀點）。</summary>
public sealed record Posting(AccountId AccountId, decimal Amount);
