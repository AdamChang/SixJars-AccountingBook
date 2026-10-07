import { BookDto, GenerateResultDto, PlannedExpenseDto, RecurringPlannedExpenseDto } from '../../core/api/dto';
import { BOOK } from '../transactions/testing/book-fixture';
import {
  categoryOptions, describeRecurrence, generationMessage, loanAccounts, plannedGroups, refreshMessage,
} from './planned-expense-rules';

// BOOK 的分類只有浮動（飲食／午餐）與收入；這裡補上固定、貸款、特別各一組
const book: BookDto = {
  ...BOOK,
  categories: [
    ...BOOK.categories,
    { id: 'fix', name: '固定支出', kind: 'Expense', nature: 'Fixed', parentId: null, sortOrder: 1, archivedAt: null },
    { id: 'ins', name: '保險費', kind: 'Expense', nature: 'Fixed', parentId: 'fix', sortOrder: 0, archivedAt: null },
    { id: 'old', name: '舊保單', kind: 'Expense', nature: 'Fixed', parentId: 'fix', sortOrder: 1, archivedAt: '2026-01-01T00:00:00Z' },
    { id: 'loan', name: '貸款支出', kind: 'Expense', nature: 'Loan', parentId: null, sortOrder: 2, archivedAt: null },
    { id: 'gift', name: '禮金', kind: 'Expense', nature: 'Special', parentId: null, sortOrder: 3, archivedAt: null },
  ],
};

const planned = (over: Partial<PlannedExpenseDto>): PlannedExpenseDto => ({
  id: 'p', budgetMonth: 202604, categoryId: 'ins', accountId: null, estimatedAmount: -100, note: null,
  paidTransactionId: null, isPaid: false, version: 1, sourceId: null, ...over,
});

describe('plannedGroups', () => {
  it('groups_by_category_nature_in_fixed_loan_special_order_and_totals_unpaid', () => {
    const groups = plannedGroups([
      planned({ id: 'g', categoryId: 'gift', estimatedAmount: -500 }),
      planned({ id: 'a', categoryId: 'ins', estimatedAmount: -100 }),
      planned({ id: 'b', categoryId: 'ins', estimatedAmount: -200, isPaid: true, paidTransactionId: 't' }),
      planned({ id: 'l', categoryId: 'loan', estimatedAmount: -3000, sourceId: 'r1' }),
    ], book);

    expect(groups.map(g => g.title)).toEqual(['固定支出', '貸款支出', '特別支出']);
    expect(groups[0].rows.map(r => [r.planned.id, r.categoryLabel, r.amount])).toEqual([
      ['a', '固定支出 › 保險費', 100], ['b', '固定支出 › 保險費', 200],
    ]);
    expect(groups[0].unpaidTotal).toBe(100);
    expect(groups[1].rows[0].isRecurring).toBe(true);
  });

  it('omits_empty_groups', () => {
    expect(plannedGroups([planned({ categoryId: 'gift' })], book).map(g => g.title)).toEqual(['特別支出']);
  });
});

describe('categoryOptions', () => {
  it('lists_usable_categories_of_given_natures_and_keeps_current_archived_one', () => {
    expect(categoryOptions(book, ['Fixed', 'Loan']).map(o => o.id)).toEqual(['fix', 'ins', 'loan']);
    expect(categoryOptions(book, ['Fixed'], 'old').map(o => o.id)).toEqual(['fix', 'ins', 'old']);
  });

  it('excludes_sub_categories_of_archived_main', () => {
    const archivedMain: BookDto = {
      ...book,
      categories: book.categories.map(c => c.id === 'fix' ? { ...c, archivedAt: '2026-01-01T00:00:00Z' } : c),
    };
    expect(categoryOptions(archivedMain, ['Fixed', 'Loan']).map(o => o.id)).toEqual(['loan']);
  });
});

describe('loanAccounts', () => {
  it('lists_unarchived_loan_accounts', () => {
    expect(loanAccounts(BOOK).map(a => a.id)).toEqual(['acc-loan']);
  });
});

describe('generationMessage', () => {
  const result = (created: number, reasons: GenerateResultDto['skipped'][number]['reason'][]): GenerateResultDto => ({
    created: Array.from({ length: created }, (_, i) => planned({ id: `p${i}` })),
    skipped: reasons.map(reason => ({ recurringId: 'r', plannedExpenseId: null, reason })),
  });

  it('summarizes_created_and_skipped_by_reason', () => {
    expect(generationMessage(result(5, ['CategoryArchived']))).toBe('建立 5 筆，略過 1 筆：分類已封存');
    expect(generationMessage(result(0, ['AlreadyGenerated', 'AlreadyGenerated', 'AccountArchived'])))
      .toBe('建立 0 筆，略過 3 筆：已產生過 2、帳戶已封存 1');
  });

  it('says_nothing_to_generate_when_both_are_empty', () => {
    expect(generationMessage(result(0, []))).toBe('本月沒有需要產生的週期項目');
  });
});

describe('refreshMessage', () => {
  it('summarizes_updated_and_not_due', () => {
    expect(refreshMessage({ updated: [planned({})], skipped: [{ recurringId: 'r', plannedExpenseId: 'p', reason: 'NotDue' }] }))
      .toBe('更新 1 筆，略過 1 筆：本月已不適用');
    expect(refreshMessage({ updated: [], skipped: [] })).toBe('沒有需要更新的預定支出');
  });
});

describe('describeRecurrence', () => {
  const item = (over: Partial<RecurringPlannedExpenseDto>): RecurringPlannedExpenseDto => ({
    id: 'r', categoryId: 'ins', accountId: null, defaultAmount: -100, note: null,
    frequency: 'Monthly', months: [], startMonth: 202601, endMonth: null, version: 1, ...over,
  });

  it('describes_frequency_and_range', () => {
    expect(describeRecurrence(item({}))).toBe('每月，2026/01 起');
    expect(describeRecurrence(item({ frequency: 'Yearly', months: [1, 7], endMonth: 202712 })))
      .toBe('每年 1、7 月，2026/01–2027/12');
  });
});
