import { AccountDto, AccountType, CategoryDto, TransactionDto, TransactionKind } from '../../core/api/dto';
import {
  KIND_RULES, MORE_KINDS, PRIMARY_KINDS, TransactionFormValue, accountsFor, categoryOptions,
  categoryKindOf, fromTransaction, isLocked, signedAmount, toTransactionInput,
} from './transaction-rules';

const TYPES: AccountType[] = ['Cash', 'Bank', 'CreditCard', 'EWallet', 'Loan'];
const sampleAccounts: AccountDto[] = TYPES.map(type => ({
  id: `a-${type}`, name: type, type, openingBalance: 0, countsAsAvailableCash: true,
}));

interface Expectation {
  label: string; account: AccountType[]; counter: AccountType[] | null; counterRequired?: boolean;
  category: boolean | null; fund: boolean; principal: boolean;
}
// 獨立於實作的期望表（來源：brief 欄位表）；category: true=必填、false=選填、null=無
const EXPECTED: Record<TransactionKind, Expectation> = {
  Income: { label: '收入', account: ['Cash', 'Bank', 'EWallet'], counter: null, category: true, fund: false, principal: false },
  Expense: { label: '支出', account: ['Cash', 'Bank', 'CreditCard', 'EWallet'], counter: null, category: true, fund: false, principal: false },
  Transfer: { label: '轉帳', account: ['Cash', 'Bank'], counter: ['Cash', 'Bank'], counterRequired: true, category: null, fund: false, principal: false },
  Withdrawal: { label: '提款', account: ['Bank'], counter: ['Cash'], counterRequired: true, category: null, fund: false, principal: false },
  CashDeposit: { label: '現金存入', account: ['Bank'], counter: ['Cash'], counterRequired: true, category: null, fund: false, principal: false },
  TopUp: { label: '加值', account: ['EWallet'], counter: ['Cash', 'Bank', 'CreditCard'], counterRequired: true, category: null, fund: false, principal: false },
  CardPayment: { label: '繳卡費', account: ['Cash', 'Bank'], counter: ['CreditCard'], counterRequired: true, category: null, fund: false, principal: false },
  LoanDisbursement: { label: '新增貸款', account: ['Cash', 'Bank'], counter: ['Loan'], counterRequired: true, category: null, fund: false, principal: false },
  LoanPayment: { label: '貸款繳款', account: ['Cash', 'Bank'], counter: ['Loan'], counterRequired: true, category: false, fund: false, principal: true },
  FundAllocation: { label: '入新資金', account: ['Cash', 'Bank', 'EWallet'], counter: ['Cash', 'Bank'], counterRequired: false, category: null, fund: true, principal: false },
  FundWithdrawal: { label: '出資金', account: ['Cash', 'Bank', 'EWallet'], counter: null, category: false, fund: true, principal: false },
  FundReturn: { label: '資金回流', account: ['Cash', 'Bank', 'EWallet'], counter: null, category: null, fund: true, principal: false },
};
const ALL_KINDS = Object.keys(EXPECTED) as TransactionKind[];

const filledValue = (kind: TransactionKind): TransactionFormValue => ({
  kind, date: new Date(2026, 0, 5), accountId: 'acc', counterAccountId: 'counter', categoryId: 'cat',
  planningFundId: 'fund', amount: 100, refund: false, loanPrincipal: 60, note: ' 備註 ', budgetMonthOverride: 202603,
});

describe('KIND_RULES', () => {
  it.each(ALL_KINDS)('%s 的標籤、帳戶類型與欄位可見性符合規格', kind => {
    const rule = KIND_RULES[kind];
    const expected = EXPECTED[kind];
    expect(rule.label).toBe(expected.label);
    expect(accountsFor(sampleAccounts, rule.account).map(a => a.type)).toEqual(expected.account);
    if (expected.counter === null) {
      expect(rule.counter).toBeNull();
    } else {
      expect(accountsFor(sampleAccounts, rule.counter!).map(a => a.type)).toEqual(expected.counter);
      expect(rule.counter!.required).toBe(expected.counterRequired);
    }
    expect(rule.category === null ? null : rule.category.required).toBe(expected.category);
    expect(rule.planningFund).toBe(expected.fund);
    expect(rule.loanPrincipal).toBe(expected.principal);
  });

  it('只有收入與支出帶正負號', () => {
    expect(ALL_KINDS.filter(k => KIND_RULES[k].signed).sort()).toEqual(['Expense', 'Income']);
  });

  it.each([
    ['Transfer', 'accountToCounter'], ['Withdrawal', 'accountToCounter'], ['CashDeposit', 'counterToAccount'],
    ['TopUp', 'counterToAccount'], ['CardPayment', 'accountToCounter'], ['LoanDisbursement', 'counterToAccount'],
    ['LoanPayment', 'accountToCounter'], ['FundAllocation', 'counterToAccount'],
    ['Income', null], ['Expense', null], ['FundWithdrawal', null], ['FundReturn', null],
  ] as [TransactionKind, string | null][])('%s 的 flow 為 %s', (kind, flow) => {
    expect(KIND_RULES[kind].flow).toBe(flow);
  });

  it('PRIMARY_KINDS 與 MORE_KINDS 合計 12 種且不重複，順序同規格', () => {
    expect(PRIMARY_KINDS).toEqual(['Expense', 'Income', 'Transfer']);
    expect(MORE_KINDS).toEqual([
      'Withdrawal', 'CashDeposit', 'TopUp', 'CardPayment', 'LoanDisbursement', 'LoanPayment',
      'FundAllocation', 'FundWithdrawal', 'FundReturn',
    ]);
    const all = [...PRIMARY_KINDS, ...MORE_KINDS];
    expect(all.length).toBe(12);
    expect(new Set(all).size).toBe(12);
  });
});

describe('accountsFor', () => {
  it('保留輸入順序', () => {
    const reversed = [...sampleAccounts].reverse();
    expect(accountsFor(reversed, KIND_RULES.Expense.account).map(a => a.type))
      .toEqual(['EWallet', 'CreditCard', 'Bank', 'Cash']);
  });
});

describe('categoryKindOf', () => {
  it('Income 為 Income，其餘為 Expense', () => {
    expect(categoryKindOf('Income')).toBe('Income');
    expect(categoryKindOf('Expense')).toBe('Expense');
    expect(categoryKindOf('LoanPayment')).toBe('Expense');
    expect(categoryKindOf('FundWithdrawal')).toBe('Expense');
  });
});

describe('signedAmount', () => {
  it.each([
    ['Expense', 100, false, -100], ['Expense', 100, true, 100],
    ['Income', 100, false, 100], ['Income', 100, true, -100],
    ['Transfer', 100, true, 100], ['LoanPayment', 100, true, 100],
  ] as [TransactionKind, number, boolean, number][])('%s %d refund=%s → %d', (kind, amount, refund, expected) => {
    expect(signedAmount(kind, amount, refund)).toBe(expected);
  });
});

describe('toTransactionInput', () => {
  it.each(ALL_KINDS)('%s：規則外的欄位為 null，規則內的保留', kind => {
    const e = EXPECTED[kind];
    const input = toTransactionInput(filledValue(kind));
    expect(input.kind).toBe(kind);
    expect(input.date).toBe('2026-01-05');
    expect(input.accountId).toBe('acc');
    expect(input.counterAccountId).toBe(e.counter ? 'counter' : null);
    expect(input.categoryId).toBe(e.category === null ? null : 'cat');
    expect(input.planningFundId).toBe(e.fund ? 'fund' : null);
    expect(input.loanPrincipal).toBe(e.principal ? 60 : null);
    expect(input.note).toBe(' 備註 ');
    expect(input.budgetMonth).toBe(202603);
  });

  it('金額依類型帶正負號', () => {
    expect(toTransactionInput(filledValue('Expense')).amount).toBe(-100);
    expect(toTransactionInput({ ...filledValue('Income'), refund: true }).amount).toBe(-100);
    expect(toTransactionInput(filledValue('Transfer')).amount).toBe(100);
  });

  it('空白備註送 null、未覆寫月份送 null、選填的 counter 保留 null', () => {
    const input = toTransactionInput({
      ...filledValue('FundAllocation'), note: '   ', budgetMonthOverride: null, counterAccountId: null,
    });
    expect(input.note).toBeNull();
    expect(input.budgetMonth).toBeNull();
    expect(input.counterAccountId).toBeNull();
  });

  it('日期用本地年月日（午夜不跨日）', () => {
    const input = toTransactionInput({ ...filledValue('Expense'), date: new Date(2026, 0, 1, 0, 30) });
    expect(input.date).toBe('2026-01-01');
  });
});

describe('fromTransaction', () => {
  const dto = (over: Partial<TransactionDto>): TransactionDto => ({
    id: 't1', kind: 'Expense', date: '2026-01-05', budgetMonth: 202601, amount: -100, accountId: 'acc',
    counterAccountId: null, categoryId: 'cat', planningFundId: null, loanPrincipal: null, loanInterest: null,
    note: null, postings: [], version: 1, ...over,
  });

  it('支出 -100 → 金額 100、非退款、月份未覆寫，且可來回', () => {
    const form = fromTransaction(dto({}));
    expect(form).toMatchObject({ kind: 'Expense', amount: 100, refund: false, budgetMonthOverride: null, note: '' });
    expect(form.date).toEqual(new Date(2026, 0, 5));
    const input = toTransactionInput(form);
    expect(input).toMatchObject({ amount: -100, budgetMonth: null, date: '2026-01-05', note: null });
  });

  it('收入 -50 → 沖回，來回後仍為 -50', () => {
    const form = fromTransaction(dto({ kind: 'Income', amount: -50 }));
    expect(form).toMatchObject({ amount: 50, refund: true });
    expect(toTransactionInput(form).amount).toBe(-50);
  });

  it('支出 +30 → 退款；收入 +30 → 非沖回', () => {
    expect(fromTransaction(dto({ amount: 30 })).refund).toBe(true);
    expect(fromTransaction(dto({ kind: 'Income', amount: 30 })).refund).toBe(false);
  });

  it('其他類型不會被判為退款', () => {
    expect(fromTransaction(dto({ kind: 'Transfer', amount: 100, counterAccountId: 'c' })).refund).toBe(false);
  });

  it('budgetMonth 與日期月份不同時保留覆寫', () => {
    const form = fromTransaction(dto({ budgetMonth: 202602 }));
    expect(form.budgetMonthOverride).toBe(202602);
    expect(toTransactionInput(form).budgetMonth).toBe(202602);
  });

  it('貸款繳款保留本金與備註', () => {
    const form = fromTransaction(dto({
      kind: 'LoanPayment', amount: 100, counterAccountId: 'loan', loanPrincipal: 60, note: '本月',
    }));
    expect(form).toMatchObject({ loanPrincipal: 60, counterAccountId: 'loan', note: '本月' });
  });
});

describe('categoryOptions', () => {
  const categories: CategoryDto[] = [
    { id: 'c1', name: '食', kind: 'Expense', nature: 'Floating', parentId: null },
    { id: 'c2', name: '早餐', kind: 'Expense', nature: 'Floating', parentId: 'c1' },
    { id: 'c3', name: '薪資', kind: 'Income', nature: null, parentId: null },
    { id: 'c4', name: '交通', kind: 'Expense', nature: 'Floating', parentId: null },
  ];

  it('主分類為「主」、子分類為「主 / 子」，維持原順序', () => {
    expect(categoryOptions(categories, 'Expense')).toEqual([
      { id: 'c1', label: '食' }, { id: 'c2', label: '食 / 早餐' }, { id: 'c4', label: '交通' },
    ]);
  });

  it('kind 不符的分類不出現', () => {
    expect(categoryOptions(categories, 'Income')).toEqual([{ id: 'c3', label: '薪資' }]);
  });
});

describe('isLocked', () => {
  it.each([
    ['2026-01-31', '2026-01-31', true], ['2026-01-30', '2026-01-31', true],
    ['2026-02-01', '2026-01-31', false], ['2026-01-01', null, false],
  ] as [string, string | null, boolean][])('%s vs lockDate %s → %s', (date, lock, expected) => {
    expect(isLocked(date, lock)).toBe(expected);
  });
});
