using SixJars.Domain.Books;
using SixJars.Domain.Planning;
using SixJars.Domain.Transactions;

namespace SixJars.Domain.Ledger;

/// <summary>計算用的唯讀帳本快照；P1 由記憶體集合提供，P2 由查詢提供。</summary>
public sealed record LedgerSnapshot(Book Book, IReadOnlyList<Transaction> Transactions, IReadOnlyList<PlannedExpense> PlannedExpenses);
