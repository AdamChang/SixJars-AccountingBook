import { AccountDto, CategoryDto, PlanningFundDto } from '../../core/api/dto';
import { accountGroup, categoryGroups, fundGroup, moveRow, reorderRequest } from './settings-rules';

const archived = '2026-05-01T00:00:00+00:00';
const cat = (id: string, name: string, kind: 'Income' | 'Expense', parentId: string | null, sortOrder: number,
  archivedAt: string | null = null): CategoryDto =>
  ({ id, name, kind, nature: kind === 'Expense' ? 'Floating' : null, parentId, sortOrder, archivedAt });

describe('settings-rules', () => {
  const categories: CategoryDto[] = [
    cat('salary', '薪資', 'Income', null, 0),
    cat('food', '飲食', 'Expense', null, 0),
    cat('lunch', '午餐', 'Expense', 'food', 0),
    cat('dinner', '晚餐', 'Expense', 'food', 1, archived),
    cat('fixed', '固定支出', 'Expense', null, 1),
  ];

  it('category_groups_are_income_mains_expense_mains_then_subs_per_main', () => {
    const groups = categoryGroups(categories);
    // 沒有子分類的主分類也有一組（空的），才能在上面新增第一個子分類
    expect(groups.map(g => g.title)).toEqual(['收入主分類', '支出主分類', '薪資 的子分類', '飲食 的子分類', '固定支出 的子分類']);
    expect(groups[1].rows.map(r => r.label)).toEqual(['飲食', '固定支出']);
    expect(groups[1].rows[0].detail).toBe('浮動');
    expect(groups[2].rows).toEqual([]);
    expect(groups[3].rows.map(r => [r.label, r.archived])).toEqual([['午餐', false], ['晚餐', true]]);
    expect(groups[3]).toMatchObject({ path: 'categories', categoryKind: 'Expense', parentId: 'food' });
  });

  it('account_group_shows_type_and_archived_state_in_sort_order', () => {
    const accounts: AccountDto[] = [
      { id: 'a2', name: '舊卡', type: 'CreditCard', openingBalance: 0, countsAsAvailableCash: false, sortOrder: 1, archivedAt: archived },
      { id: 'a1', name: '現金', type: 'Cash', openingBalance: 0, countsAsAvailableCash: true, sortOrder: 0, archivedAt: null },
    ];
    const group = accountGroup(accounts);
    expect(group.rows.map(r => [r.label, r.detail, r.archived])).toEqual([['現金', '現金', false], ['舊卡', '信用卡', true]]);
    expect(group).toMatchObject({ path: 'accounts', categoryKind: null, parentId: null });
  });

  it('fund_group_has_no_detail', () => {
    const funds: PlanningFundDto[] = [{ id: 'f1', name: '旅遊基金', openingBalance: 0, sortOrder: 0, archivedAt: null }];
    expect(fundGroup(funds)).toMatchObject({ path: 'planning-funds', rows: [{ id: 'f1', label: '旅遊基金', detail: '', archived: false }] });
  });

  it('move_row_returns_new_array', () => {
    const ids = ['a', 'b', 'c'];
    expect(moveRow(ids, 2, 0)).toEqual(['c', 'a', 'b']);
    expect(ids).toEqual(['a', 'b', 'c']);
  });

  it('reorder_request_appends_archived_members_of_the_group', () => {
    const group = categoryGroups(categories)[3];
    expect(reorderRequest(group, ['lunch'])).toEqual({ kind: 'Expense', parentId: 'food', ids: ['lunch', 'dinner'] });
    const mains = categoryGroups(categories)[1];
    expect(reorderRequest(mains, ['fixed', 'food'])).toEqual({ kind: 'Expense', parentId: null, ids: ['fixed', 'food'] });
    expect(reorderRequest(fundGroup([]), [])).toEqual({ ids: [] });
  });
});
