import { moveItemInArray } from '@angular/cdk/drag-drop';
import {
  AccountDto, AccountType, CategoryDto, CategoryKind, ExpenseNature, PlanningFundDto, ReorderBody, SettingPath,
} from '../../core/api/dto';

export interface SettingRow { id: string; label: string; detail: string; archived: boolean }

// 一組 = 一次排序的範圍（與後端 Book 的分組相同）：帳戶、財務規劃帳戶、某種類的主分類、某主分類的子分類
export interface SettingGroup {
  key: string; title: string; path: SettingPath; categoryKind: CategoryKind | null; parentId: string | null; rows: SettingRow[];
}

export const ACCOUNT_TYPE_LABELS: Record<AccountType, string> = {
  Cash: '現金', Bank: '銀行', CreditCard: '信用卡', EWallet: '電子錢包', Loan: '貸款',
};
export const NATURE_LABELS: Record<ExpenseNature, string> = { Floating: '浮動', Fixed: '固定', Loan: '貸款', Special: '特別' };

const bySortOrder = <T extends { sortOrder: number }>(items: T[]): T[] => [...items].sort((a, b) => a.sortOrder - b.sortOrder);

export function accountGroup(accounts: AccountDto[]): SettingGroup {
  return {
    key: 'accounts', title: '帳戶', path: 'accounts', categoryKind: null, parentId: null,
    rows: bySortOrder(accounts).map(a => ({ id: a.id, label: a.name, detail: ACCOUNT_TYPE_LABELS[a.type], archived: a.archivedAt !== null })),
  };
}

export function fundGroup(funds: PlanningFundDto[]): SettingGroup {
  return {
    key: 'planning-funds', title: '財務規劃帳戶', path: 'planning-funds', categoryKind: null, parentId: null,
    rows: bySortOrder(funds).map(f => ({ id: f.id, label: f.name, detail: '', archived: f.archivedAt !== null })),
  };
}

// 收入主分類、支出主分類，接著每個主分類各一組子分類（沒有子分類也要有空的一組，才能新增第一個）
export function categoryGroups(categories: CategoryDto[]): SettingGroup[] {
  const row = (c: CategoryDto): SettingRow => ({
    id: c.id, label: c.name, detail: c.nature ? NATURE_LABELS[c.nature] : '', archived: c.archivedAt !== null,
  });
  const mains = (kind: CategoryKind) => bySortOrder(categories.filter(c => c.parentId === null && c.kind === kind));
  const mainGroup = (kind: CategoryKind, title: string): SettingGroup =>
    ({ key: `main-${kind}`, title, path: 'categories', categoryKind: kind, parentId: null, rows: mains(kind).map(row) });
  const subGroups = [...mains('Income'), ...mains('Expense')].map((main): SettingGroup => ({
    key: `sub-${main.id}`, title: `${main.name} 的子分類`, path: 'categories', categoryKind: main.kind, parentId: main.id,
    rows: bySortOrder(categories.filter(c => c.parentId === main.id)).map(row),
  }));
  return [mainGroup('Income', '收入主分類'), mainGroup('Expense', '支出主分類'), ...subGroups];
}

export function moveRow(ids: string[], from: number, to: number): string[] {
  const copy = [...ids];
  moveItemInArray(copy, from, to);
  return copy;
}

// 後端要求整組完全相同（含已封存）；畫面只排未封存的列，已封存的依原順序接在最後
export function reorderRequest(group: SettingGroup, activeIds: string[]): ReorderBody {
  const ids = [...activeIds, ...group.rows.filter(r => r.archived).map(r => r.id)];
  return group.path === 'categories' ? { kind: group.categoryKind, parentId: group.parentId, ids } : { ids };
}
