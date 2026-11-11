namespace SixJars.Domain.Transactions;

/// <summary>系統定義、不可自訂的交易機制，決定交易會產生哪些分錄。</summary>
public enum TransactionKind
{
    Income, Expense, Transfer, Withdrawal, CashDeposit, TopUp, CardPayment,
    LoanDisbursement, LoanPayment, FundAllocation, FundWithdrawal, FundReturn,

    /// <summary>餘額調整：只由補匯舊年度在銜接點產生（spec M3），不計入月可用餘額與收支報表。</summary>
    BalanceAdjustment,
}
