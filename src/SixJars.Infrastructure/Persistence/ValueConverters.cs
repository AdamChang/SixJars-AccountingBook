using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SixJars.Domain.Common;

namespace SixJars.Infrastructure.Persistence;

// 強型別 Id 一律存成 uuid；歸屬月份存成 yyyymm 整數（BudgetMonth.Key）。

internal sealed class BookIdConverter() : ValueConverter<BookId, Guid>(id => id.Value, value => new BookId(value));

internal sealed class AccountIdConverter() : ValueConverter<AccountId, Guid>(id => id.Value, value => new AccountId(value));

internal sealed class PlanningFundIdConverter() : ValueConverter<PlanningFundId, Guid>(id => id.Value, value => new PlanningFundId(value));

internal sealed class CategoryIdConverter() : ValueConverter<CategoryId, Guid>(id => id.Value, value => new CategoryId(value));

internal sealed class TransactionIdConverter() : ValueConverter<TransactionId, Guid>(id => id.Value, value => new TransactionId(value));

internal sealed class PlannedExpenseIdConverter() : ValueConverter<PlannedExpenseId, Guid>(id => id.Value, value => new PlannedExpenseId(value));

internal sealed class BudgetMonthConverter() : ValueConverter<BudgetMonth, int>(month => month.Key, key => BudgetMonth.FromKey(key));
