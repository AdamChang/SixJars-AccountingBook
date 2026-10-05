import { BookDto, TransactionDto, TransactionInput } from '../../../core/api/dto';
import { budgetMonthOf } from '../../../shared/dates';

// 每種帳戶類型各一個、收入與支出分類各有主／子、一個財務規劃帳戶；H3–H6 共用
export const BOOK: BookDto = {
  id: 'book-1',
  name: '我的帳本',
  openingDate: '2026-01-01',
  lockDate: null,
  accounts: [
    { id: 'acc-cash', name: '現金', type: 'Cash', openingBalance: 0, countsAsAvailableCash: true },
    { id: 'acc-bank', name: '銀行', type: 'Bank', openingBalance: 0, countsAsAvailableCash: true },
    { id: 'acc-card', name: '信用卡', type: 'CreditCard', openingBalance: 0, countsAsAvailableCash: false },
    { id: 'acc-ewallet', name: '悠遊卡', type: 'EWallet', openingBalance: 0, countsAsAvailableCash: false },
    { id: 'acc-loan', name: '房貸', type: 'Loan', openingBalance: 0, countsAsAvailableCash: false },
  ],
  planningFunds: [{ id: 'fund-travel', name: '旅遊基金', openingBalance: 0 }],
  categories: [
    { id: 'cat-food', name: '飲食', kind: 'Expense', nature: 'Floating', parentId: null },
    { id: 'cat-lunch', name: '午餐', kind: 'Expense', nature: 'Floating', parentId: 'cat-food' },
    { id: 'cat-salary', name: '薪資', kind: 'Income', nature: null, parentId: null },
    { id: 'cat-bonus', name: '獎金', kind: 'Income', nature: null, parentId: 'cat-salary' },
  ],
};

// 模擬後端 201 回應：把送出的 input 原樣轉成 TransactionDto
export function transactionDtoFrom(input: TransactionInput, overrides: Partial<TransactionDto> = {}): TransactionDto {
  return {
    id: 'tx-1',
    kind: input.kind,
    date: input.date,
    budgetMonth: input.budgetMonth ?? budgetMonthOf(input.date),
    amount: input.amount,
    accountId: input.accountId,
    counterAccountId: input.counterAccountId,
    categoryId: input.categoryId,
    planningFundId: input.planningFundId,
    loanPrincipal: input.loanPrincipal,
    loanInterest: input.loanPrincipal === null ? null : input.amount - input.loanPrincipal,
    note: input.note,
    postings: [],
    version: 1,
    ...overrides,
  };
}
