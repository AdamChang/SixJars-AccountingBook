namespace SixJars.Domain.Transactions;

/// <summary>系統定義、不可自訂的交易機制，決定交易會產生哪些分錄。</summary>
public enum TransactionKind
{
    Income, Expense, Transfer, Withdrawal, CashDeposit, TopUp, CardPayment,
    LoanDisbursement, LoanPayment, FundAllocation, FundWithdrawal, FundReturn,
}
