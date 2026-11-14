using ModelContextProtocol;
using SixJars.Application.Transactions;
using SixJars.Domain.Transactions;

namespace SixJars.Api.Mcp;

/// <summary>
/// 把 tool 的參數轉成 <see cref="TransactionInput"/>（spec D14）：tool 的 amount 一律是收據上的正數，
/// Domain 的 Expense 金額則是帶號的（支出為負、退款為正）。轉換只在這一層做，Domain 與 <c>TransactionInput</c> 都不動。
/// </summary>
internal static class McpTransactionArgs
{
    public static TransactionInput ToInput(
        string kind, DateOnly date, decimal amount, bool refund, Guid accountId, Guid? counterAccountId,
        Guid? categoryId, int? budgetMonth, string? note, IReadOnlyList<Guid>? tagIds)
    {
        var parsed = ParseKind(kind);

        // 先擋非正數：不讓 Claude 用負數（或 0）繞過方向的判斷。
        if (amount <= 0)
        {
            throw new McpException("amount 必須是正數（收據上的數字）；支出與收入的方向由 kind 決定，退款或折抵請設 refund 為 true。");
        }

        if (refund && parsed != TransactionKind.Expense)
        {
            throw new McpException("refund 只能用在 Expense（支出的退款或折抵）。");
        }

        var signed = parsed == TransactionKind.Expense && !refund ? -amount : amount;
        return new TransactionInput(parsed, date, budgetMonth, signed, accountId, counterAccountId, categoryId, null, null, note, tagIds);
    }

    private static TransactionKind ParseKind(string kind)
    {
        // Enum.TryParse 也吃數字字串；再用白名單比對，確保只有支援的種類能通過。
        if (Enum.TryParse<TransactionKind>(kind?.Trim(), ignoreCase: true, out var parsed) && RecordTransaction.SupportedKinds.Contains(parsed))
        {
            return parsed;
        }

        throw new McpException($"不支援的交易種類，只能記 {string.Join("、", RecordTransaction.SupportedKinds)}。");
    }
}
