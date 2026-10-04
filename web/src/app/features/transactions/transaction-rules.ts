import {
  AccountDto, AccountType, CategoryDto, CategoryKind, TransactionDto, TransactionInput, TransactionKind,
} from '../../core/api/dto';
import { budgetMonthOf, parseDateString, toDateString } from '../../shared/dates';

// 這些規則只是 UX 篩選；後端 422 仍是最終判定依據。
export interface AccountSlot { label: string; types: AccountType[] }
export interface KindRule {
  label: string;
  account: AccountSlot;
  counter: (AccountSlot & { required: boolean }) | null;
  category: { required: boolean } | null;     // 分類種類由 categoryKindOf 決定
  planningFund: boolean;                       // true = 必填
  loanPrincipal: boolean;                      // true = 必填
  signed: boolean;                             // 收入、支出：帶正負號
  flow: 'accountToCounter' | 'counterToAccount' | null;   // 列表的「從 → 到」
}

const CASH_BANK: AccountType[] = ['Cash', 'Bank'];
const CASH_BANK_EWALLET: AccountType[] = ['Cash', 'Bank', 'EWallet'];

export const KIND_RULES: Record<TransactionKind, KindRule> = {
  Income: {
    label: '收入', account: { label: '帳戶', types: CASH_BANK_EWALLET }, counter: null,
    category: { required: true }, planningFund: false, loanPrincipal: false, signed: true, flow: null,
  },
  Expense: {
    label: '支出', account: { label: '帳戶', types: ['Cash', 'Bank', 'CreditCard', 'EWallet'] }, counter: null,
    category: { required: true }, planningFund: false, loanPrincipal: false, signed: true, flow: null,
  },
  Transfer: {
    label: '轉帳', account: { label: '從', types: CASH_BANK },
    counter: { label: '到', types: CASH_BANK, required: true },
    category: null, planningFund: false, loanPrincipal: false, signed: false, flow: 'accountToCounter',
  },
  Withdrawal: {
    label: '提款', account: { label: '銀行', types: ['Bank'] },
    counter: { label: '現金', types: ['Cash'], required: true },
    category: null, planningFund: false, loanPrincipal: false, signed: false, flow: 'accountToCounter',
  },
  CashDeposit: {
    label: '現金存入', account: { label: '銀行', types: ['Bank'] },
    counter: { label: '現金', types: ['Cash'], required: true },
    category: null, planningFund: false, loanPrincipal: false, signed: false, flow: 'counterToAccount',
  },
  TopUp: {
    label: '加值', account: { label: '電子錢包', types: ['EWallet'] },
    counter: { label: '來源', types: ['Cash', 'Bank', 'CreditCard'], required: true },
    category: null, planningFund: false, loanPrincipal: false, signed: false, flow: 'counterToAccount',
  },
  CardPayment: {
    label: '繳卡費', account: { label: '付款帳戶', types: CASH_BANK },
    counter: { label: '信用卡', types: ['CreditCard'], required: true },
    category: null, planningFund: false, loanPrincipal: false, signed: false, flow: 'accountToCounter',
  },
  LoanDisbursement: {
    label: '新增貸款', account: { label: '入帳帳戶', types: CASH_BANK },
    counter: { label: '貸款', types: ['Loan'], required: true },
    category: null, planningFund: false, loanPrincipal: false, signed: false, flow: 'counterToAccount',
  },
  LoanPayment: {
    label: '貸款繳款', account: { label: '付款帳戶', types: CASH_BANK },
    counter: { label: '貸款', types: ['Loan'], required: true },
    category: { required: false }, planningFund: false, loanPrincipal: true, signed: false, flow: 'accountToCounter',
  },
  FundAllocation: {
    label: '入新資金', account: { label: '轉入帳戶', types: CASH_BANK_EWALLET },
    counter: { label: '轉出帳戶', types: CASH_BANK, required: false },
    category: null, planningFund: true, loanPrincipal: false, signed: false, flow: 'counterToAccount',
  },
  FundWithdrawal: {
    label: '出資金', account: { label: '帳戶', types: CASH_BANK_EWALLET }, counter: null,
    category: { required: false }, planningFund: true, loanPrincipal: false, signed: false, flow: null,
  },
  FundReturn: {
    label: '資金回流', account: { label: '帳戶', types: CASH_BANK_EWALLET }, counter: null,
    category: null, planningFund: true, loanPrincipal: false, signed: false, flow: null,
  },
};

export const PRIMARY_KINDS: TransactionKind[] = ['Expense', 'Income', 'Transfer'];
export const MORE_KINDS: TransactionKind[] = [
  'Withdrawal', 'CashDeposit', 'TopUp', 'CardPayment', 'LoanDisbursement', 'LoanPayment',
  'FundAllocation', 'FundWithdrawal', 'FundReturn',
];

export interface TransactionFormValue {
  kind: TransactionKind; date: Date; accountId: string; counterAccountId: string | null;
  categoryId: string | null; planningFundId: string | null; amount: number; refund: boolean;
  loanPrincipal: number | null; note: string; budgetMonthOverride: number | null;
}

export function categoryKindOf(kind: TransactionKind): CategoryKind {
  return kind === 'Income' ? 'Income' : 'Expense';
}

export function signedAmount(kind: TransactionKind, amount: number, refund: boolean): number {
  if (kind === 'Expense') {
    return refund ? amount : -amount;
  }
  if (kind === 'Income') {
    return refund ? -amount : amount;
  }
  return amount;
}

// 規則外的欄位一律送 null：使用者切換類型後，隱藏欄位可能還殘留舊值
export function toTransactionInput(value: TransactionFormValue): TransactionInput {
  const rule = KIND_RULES[value.kind];
  return {
    kind: value.kind,
    date: toDateString(value.date),
    budgetMonth: value.budgetMonthOverride,
    amount: signedAmount(value.kind, value.amount, value.refund),
    accountId: value.accountId,
    counterAccountId: rule.counter ? value.counterAccountId : null,
    categoryId: rule.category ? value.categoryId : null,
    planningFundId: rule.planningFund ? value.planningFundId : null,
    loanPrincipal: rule.loanPrincipal ? value.loanPrincipal : null,
    note: value.note.trim() === '' ? null : value.note,
  };
}

export function fromTransaction(dto: TransactionDto): TransactionFormValue {
  return {
    kind: dto.kind,
    date: parseDateString(dto.date),
    accountId: dto.accountId,
    counterAccountId: dto.counterAccountId,
    categoryId: dto.categoryId,
    planningFundId: dto.planningFundId,
    amount: Math.abs(dto.amount),
    refund: (dto.kind === 'Expense' && dto.amount > 0) || (dto.kind === 'Income' && dto.amount < 0),
    loanPrincipal: dto.loanPrincipal,
    note: dto.note ?? '',
    budgetMonthOverride: dto.budgetMonth === budgetMonthOf(dto.date) ? null : dto.budgetMonth,
  };
}

export function accountsFor(accounts: AccountDto[], slot: AccountSlot): AccountDto[] {
  return accounts.filter(account => slot.types.includes(account.type));
}

export function categoryOptions(categories: CategoryDto[], kind: CategoryKind): { id: string; label: string }[] {
  const nameById = new Map(categories.map(category => [category.id, category.name]));
  return categories
    .filter(category => category.kind === kind)
    .map(category => {
      const parentName = category.parentId === null ? undefined : nameById.get(category.parentId);
      return { id: category.id, label: parentName === undefined ? category.name : `${parentName} / ${category.name}` };
    });
}

// yyyy-MM-dd 字串可直接字典序比較
export function isLocked(date: string, lockDate: string | null): boolean {
  return lockDate !== null && date <= lockDate;
}
