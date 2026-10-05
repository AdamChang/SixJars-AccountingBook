// 手寫對照後端 record：Guid → string、DateOnly → 'yyyy-MM-dd'、decimal → number。
export type TransactionKind =
  | 'Income' | 'Expense' | 'Transfer' | 'Withdrawal' | 'CashDeposit' | 'TopUp'
  | 'CardPayment' | 'LoanDisbursement' | 'LoanPayment' | 'FundAllocation'
  | 'FundWithdrawal' | 'FundReturn';
export type AccountType = 'Cash' | 'Bank' | 'CreditCard' | 'EWallet' | 'Loan';
export type CategoryKind = 'Income' | 'Expense';
export type ExpenseNature = 'Floating' | 'Fixed' | 'Loan' | 'Special';

export interface BookSummaryDto { id: string; name: string }
export interface MeDto { subject: string; email: string | null; books: BookSummaryDto[] }

export interface AccountDto {
  id: string; name: string; type: AccountType; openingBalance: number; countsAsAvailableCash: boolean;
}
export interface PlanningFundDto { id: string; name: string; openingBalance: number }
export interface CategoryDto {
  id: string; name: string; kind: CategoryKind; nature: ExpenseNature | null; parentId: string | null;
}
export interface BookDto {
  id: string; name: string; openingDate: string; lockDate: string | null;
  accounts: AccountDto[]; planningFunds: PlanningFundDto[]; categories: CategoryDto[];
}

export interface PostingDto { accountId: string; amount: number }
export interface TransactionDto {
  id: string; kind: TransactionKind; date: string; budgetMonth: number; amount: number;
  accountId: string; counterAccountId: string | null; categoryId: string | null;
  planningFundId: string | null; loanPrincipal: number | null; loanInterest: number | null;
  note: string | null; postings: PostingDto[]; version: number;
}
export interface TransactionInput {
  kind: TransactionKind; date: string; budgetMonth: number | null; amount: number;
  accountId: string; counterAccountId: string | null; categoryId: string | null;
  planningFundId: string | null; loanPrincipal: number | null; note: string | null;
}

export interface BalanceDto { id: string; name: string; balance: number }
export interface LedgerSummaryDto {
  budgetMonth: number; asOf: string; monthlyDisposable: number; yearToDate: number;
  availableCash: number; availableCashWithEWallets: number;
  accounts: BalanceDto[]; planningFunds: BalanceDto[];
}
