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

// sortOrder：組內順序（0 起算且連續）；archivedAt：封存時間，null 表示未封存
export interface AccountDto {
  id: string; name: string; type: AccountType; openingBalance: number; countsAsAvailableCash: boolean;
  sortOrder: number; archivedAt: string | null;
}
export interface PlanningFundDto { id: string; name: string; openingBalance: number; sortOrder: number; archivedAt: string | null }
export interface CategoryDto {
  id: string; name: string; kind: CategoryKind; nature: ExpenseNature | null; parentId: string | null;
  sortOrder: number; archivedAt: string | null;
}

// 設定的路徑片段，與後端 BooksEndpoints 的 MapSettingActions 一致
export type SettingPath = 'accounts' | 'planning-funds' | 'categories';
// 一次送出整組的新順序（含已封存）；kind、parentId 只用於分類
export interface ReorderBody { kind?: CategoryKind | null; parentId?: string | null; ids: string[] }
export interface AddAccountBody { name: string; type: AccountType; openingBalance: number; countsAsAvailableCash: boolean }
export interface AddPlanningFundBody { name: string; openingBalance: number }
export interface AddCategoryBody { name: string; kind: CategoryKind; nature: ExpenseNature | null; parentId: string | null }
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

// 預定支出與週期預定支出：金額沿用支出的符號慣例（負數）
export interface PlannedExpenseInput {
  budgetMonth: number; categoryId: string; accountId: string | null; estimatedAmount: number; note: string | null;
}
export interface PlannedExpenseDto extends PlannedExpenseInput {
  id: string; paidTransactionId: string | null; isPaid: boolean; version: number; sourceId: string | null;
}
export interface PayPlannedExpenseBody {
  version: number; date: string; accountId: string; amount: number; loanAccountId: string | null; loanPrincipal: number | null;
}
export type RecurrenceFrequency = 'Monthly' | 'Yearly';
export interface RecurringPlannedExpenseInput {
  categoryId: string; accountId: string | null; defaultAmount: number; note: string | null;
  frequency: RecurrenceFrequency; months: number[]; startMonth: number; endMonth: number | null;
}
export interface RecurringPlannedExpenseDto extends RecurringPlannedExpenseInput { id: string; version: number }
export type RecurringSkipReason = 'AlreadyGenerated' | 'CategoryArchived' | 'AccountArchived' | 'NotDue';
export interface RecurringSkipDto { recurringId: string; plannedExpenseId: string | null; reason: RecurringSkipReason }
export interface GenerateResultDto { created: PlannedExpenseDto[]; skipped: RecurringSkipDto[] }
export interface RefreshResultDto { updated: PlannedExpenseDto[]; skipped: RecurringSkipDto[] }
