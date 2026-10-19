using SixJars.Domain.Common;

namespace SixJars.Domain.Transactions;

/// <summary>交易掛著的一個標籤（ADR 0011）。交易上的標籤是集合，不記錄勾選順序（spec G20）。</summary>
public sealed record TransactionTag(TagId TagId);
