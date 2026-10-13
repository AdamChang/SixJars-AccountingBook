// 手寫對照後端 record：Guid → string、DateOnly → 'yyyy-MM-dd'、decimal → number。
export type TransactionKind =
  | 'Income' | 'Expense' | 'Transfer' | 'Withdrawal' | 'CashDeposit' | 'TopUp'
  | 'CardPayment' | 'LoanDisbursement' | 'LoanPayment' | 'FundAllocation'
  | 'FundWithdrawal' | 'FundReturn';
export type AccountType = 'Cash' | 'Bank' | 'CreditCard' | 'EWallet' | 'Loan';
export type CategoryKind = 'Income' | 'Expense';
export type ExpenseNature = 'Floating' | 'Fixed' | 'Loan' | 'Special';
// 標籤的固定色盤，順序與後端 enum TagColor 相同（tags spec G22）
export type TagColor =
  | 'Red' | 'Orange' | 'Amber' | 'Yellow' | 'Lime' | 'Green' | 'Teal' | 'Cyan' | 'Blue' | 'Indigo' | 'Purple' | 'Pink';

export interface BookSummaryDto { id: string; name: string }
// canCreateBooks：是否在建立者名單；remainingBookQuota：還能建立幾本，非建立者為 0（後端 spec D8，前端只負責顯示）
export interface MeDto {
  subject: string; email: string | null; books: BookSummaryDto[]; canCreateBooks: boolean; remainingBookQuota: number;
}

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
export interface TagDto { id: string; name: string; color: TagColor; sortOrder: number; archivedAt: string | null }

// 設定的路徑片段，與後端 BooksEndpoints 的 MapSettingActions 一致
export type SettingPath = 'accounts' | 'planning-funds' | 'categories' | 'tags';
// 一次送出整組的新順序（含已封存）；kind、parentId 只用於分類
export interface ReorderBody { kind?: CategoryKind | null; parentId?: string | null; ids: string[] }
export interface AddAccountBody { name: string; type: AccountType; openingBalance: number; countsAsAvailableCash: boolean }
export interface AddPlanningFundBody { name: string; openingBalance: number }
export interface AddCategoryBody { name: string; kind: CategoryKind; nature: ExpenseNature | null; parentId: string | null }
export interface AddTagBody { name: string; color: TagColor }
export interface BookDto {
  id: string; name: string; openingDate: string; lockDate: string | null;
  accounts: AccountDto[]; planningFunds: PlanningFundDto[]; categories: CategoryDto[]; tags: TagDto[];
}
export interface CreateBookBody { name: string; openingDate: string }

// 刪除帳本前的統計；transactions、plannedExpenses 只算未刪除的（後端 spec D3）
export interface BookDeletionPreviewDto {
  name: string; openingDate: string; lockDate: string | null;
  transactions: number; plannedExpenses: number; recurringPlannedExpenses: number;
  categoryBudgets: number; members: number; auditEntries: number;
}

export interface PostingDto { accountId: string; amount: number }
export interface TransactionDto {
  id: string; kind: TransactionKind; date: string; budgetMonth: number; amount: number;
  accountId: string; counterAccountId: string | null; categoryId: string | null;
  planningFundId: string | null; loanPrincipal: number | null; loanInterest: number | null;
  note: string | null; postings: PostingDto[]; version: number;
  tagIds: string[];   // 依 Guid 排序；顯示時依標籤的 sortOrder（後端 D5）
}
export interface TransactionInput {
  kind: TransactionKind; date: string; budgetMonth: number | null; amount: number;
  accountId: string; counterAccountId: string | null; categoryId: string | null;
  planningFundId: string | null; loanPrincipal: number | null; note: string | null;
  tagIds: string[];   // 一律送出：後端把沒帶視為清空（後端 D4）
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

// 預算（P4 段 L）。金額一律為正數；budget／source／remaining 為 null 表示該月未設
export type BudgetSource = 'Default' | 'Override';
export interface BudgetRowDto {
  categoryId: string; defaultAmount: number | null; budget: number | null; source: BudgetSource | null;
  actual: number; remaining: number | null;
}
export interface BudgetTotalsDto { budget: number; actual: number; remaining: number }
export interface BudgetSheetDto { rows: BudgetRowDto[]; totals: BudgetTotalsDto }
export interface BudgetOverrideDto { budgetMonth: number; amount: number }
export interface CategoryBudgetDto { categoryId: string; defaultAmount: number | null; overrides: BudgetOverrideDto[] }

export type ReportSectionKind = 'Income' | 'Floating' | 'Fixed' | 'Loan' | 'Special';
// Unassigned 是「（未分子類）」，id 與它的主分類相同
export type ReportLineKind = 'Category' | 'Unassigned' | 'LoanAccount' | 'PlanningFund';
export interface ReportLineDto {
  kind: ReportLineKind; id: string; name: string; amount: number;
  principal: number | null; interest: number | null; children: ReportLineDto[];
}
export interface ReportSectionDto { kind: ReportSectionKind; total: number; unpaid: number | null; lines: ReportLineDto[] }
export interface PlanningLineDto { fundId: string; name: string; allocation: number; withdrawal: number; return: number }
export interface MonthlyReportDto {
  budgetMonth: number; monthlyDisposable: number; income: ReportSectionDto; expenses: ReportSectionDto[];
  expenseTotal: number; unpaidTotal: number; planning: PlanningLineDto[];
}
export interface YearlyFiguresDto { months: number[]; total: number; shareOfIncome: number | null; monthlyAverage: number | null }
export interface YearlyLineDto { kind: ReportLineKind; id: string; name: string; figures: YearlyFiguresDto; children: YearlyLineDto[] }
export interface YearlySectionDto { kind: ReportSectionKind; figures: YearlyFiguresDto; lines: YearlyLineDto[] }
export interface YearlyPlanningDto { allocation: YearlyLineDto[]; withdrawal: YearlyLineDto[]; return: YearlyLineDto[] }
export interface YearlyReportDto {
  // averageFromMonth：月平均從幾月算起（開帳那一年是開帳月份），沒有月平均時為 null
  year: number; averageDivisor: number | null; averageFromMonth: number | null; income: YearlySectionDto; expenses: YearlySectionDto[];
  expenseTotal: YearlyFiguresDto; planning: YearlyPlanningDto;
}
