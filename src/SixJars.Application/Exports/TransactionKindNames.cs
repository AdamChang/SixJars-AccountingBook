using SixJars.Domain.Transactions;

namespace SixJars.Application.Exports;

/// <summary>交易類型在匯出檔中顯示的中文名稱（CONTEXT.md「交易類型」）。</summary>
public static class TransactionKindNames
{
    public static string Of(TransactionKind kind) => kind switch
    {
        TransactionKind.Income => "收入",
        TransactionKind.Expense => "支出",
        TransactionKind.Transfer => "轉帳",
        TransactionKind.Withdrawal => "提款",
        TransactionKind.CashDeposit => "現金存入",
        TransactionKind.TopUp => "加值",
        TransactionKind.CardPayment => "繳卡費",
        TransactionKind.LoanDisbursement => "新增貸款",
        TransactionKind.LoanPayment => "貸款繳款",
        TransactionKind.FundAllocation => "入新資金",
        TransactionKind.FundWithdrawal => "出資金",
        TransactionKind.FundReturn => "資金回流",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "沒有對應中文名稱的交易類型。"),
    };
}
