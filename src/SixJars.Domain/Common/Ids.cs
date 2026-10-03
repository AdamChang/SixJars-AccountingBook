namespace SixJars.Domain.Common;

public readonly record struct BookId(Guid Value) { public static BookId New() => new(Guid.CreateVersion7()); }
public readonly record struct AccountId(Guid Value) { public static AccountId New() => new(Guid.CreateVersion7()); }
public readonly record struct PlanningFundId(Guid Value) { public static PlanningFundId New() => new(Guid.CreateVersion7()); }
public readonly record struct CategoryId(Guid Value) { public static CategoryId New() => new(Guid.CreateVersion7()); }
public readonly record struct TransactionId(Guid Value) { public static TransactionId New() => new(Guid.CreateVersion7()); }
public readonly record struct PlannedExpenseId(Guid Value) { public static PlannedExpenseId New() => new(Guid.CreateVersion7()); }
