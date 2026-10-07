import {
  AccountDto, BookDto, CategoryDto, ExpenseNature, GenerateResultDto, PlannedExpenseDto, RecurringPlannedExpenseDto,
  RecurringSkipDto, RecurringSkipReason, RefreshResultDto,
} from '../../core/api/dto';
import { formatBudgetMonth } from '../../shared/dates';

export interface PlannedRow {
  planned: PlannedExpenseDto;
  categoryLabel: string;
  accountName: string | null;
  amount: number;          // 正數，顯示用
  isRecurring: boolean;
}

export interface PlannedGroup { nature: ExpenseNature; title: string; rows: PlannedRow[]; unpaidTotal: number }

const GROUP_ORDER: { nature: ExpenseNature; title: string }[] = [
  { nature: 'Fixed', title: '固定支出' },
  { nature: 'Loan', title: '貸款支出' },
  { nature: 'Special', title: '特別支出' },
];

const SKIP_LABELS: Record<RecurringSkipReason, string> = {
  AlreadyGenerated: '已產生過',
  CategoryArchived: '分類已封存',
  AccountArchived: '帳戶已封存',
  NotDue: '本月已不適用',
};

export function categoryLabel(book: BookDto, categoryId: string): string {
  const category = book.categories.find(c => c.id === categoryId);
  if (!category) return '';
  const parent = category.parentId ? book.categories.find(c => c.id === category.parentId) : undefined;
  return parent ? `${parent.name} › ${category.name}` : category.name;
}

// 預定支出依分類的支出性質分組，順序固定為固定、貸款、特別；空的組不列出
export function plannedGroups(planned: PlannedExpenseDto[], book: BookDto): PlannedGroup[] {
  const natureOf = (categoryId: string) => book.categories.find(c => c.id === categoryId)?.nature ?? null;
  return GROUP_ORDER
    .map(({ nature, title }) => {
      const rows = planned
        .filter(p => natureOf(p.categoryId) === nature)
        .map(p => ({
          planned: p,
          categoryLabel: categoryLabel(book, p.categoryId),
          accountName: book.accounts.find(a => a.id === p.accountId)?.name ?? null,
          amount: Math.abs(p.estimatedAmount),
          isRecurring: p.sourceId !== null,
        }));
      const unpaidTotal = rows.filter(r => !r.planned.isPaid).reduce((sum, r) => sum + r.amount, 0);
      return { nature, title, rows, unpaidTotal };
    })
    .filter(group => group.rows.length > 0);
}

// 自己與主分類都未封存才可選；keep 是編輯中正在使用的分類，即使已封存也保留
export function categoryOptions(book: BookDto, natures: ExpenseNature[], keep?: string): CategoryDto[] {
  const byId = new Map(book.categories.map(c => [c.id, c]));
  const usable = (c: CategoryDto) =>
    c.archivedAt === null && (c.parentId === null || byId.get(c.parentId)?.archivedAt === null);
  const ordered: CategoryDto[] = [];
  const mains = book.categories
    .filter(c => c.kind === 'Expense' && c.parentId === null && c.nature !== null && natures.includes(c.nature))
    .sort((a, b) => a.sortOrder - b.sortOrder);
  for (const main of mains) {
    ordered.push(main);
    ordered.push(...book.categories.filter(c => c.parentId === main.id).sort((a, b) => a.sortOrder - b.sortOrder));
  }
  return ordered.filter(c => usable(c) || c.id === keep);
}

export function loanAccounts(book: BookDto): AccountDto[] {
  return book.accounts.filter(a => a.type === 'Loan' && a.archivedAt === null);
}

export function generationMessage(result: GenerateResultDto): string {
  if (result.created.length === 0 && result.skipped.length === 0) return '本月沒有需要產生的週期項目';
  return `建立 ${result.created.length} 筆${skippedSuffix(result.skipped)}`;
}

export function refreshMessage(result: RefreshResultDto): string {
  if (result.updated.length === 0 && result.skipped.length === 0) return '沒有需要更新的預定支出';
  return `更新 ${result.updated.length} 筆${skippedSuffix(result.skipped)}`;
}

// 單一原因只寫原因；多種原因時附上各自的筆數
function skippedSuffix(skipped: RecurringSkipDto[]): string {
  if (skipped.length === 0) return '';
  const counts = new Map<RecurringSkipReason, number>();
  for (const { reason } of skipped) counts.set(reason, (counts.get(reason) ?? 0) + 1);
  const reasons = counts.size === 1
    ? SKIP_LABELS[[...counts.keys()][0]]
    : [...counts].map(([reason, count]) => `${SKIP_LABELS[reason]} ${count}`).join('、');
  return `，略過 ${skipped.length} 筆：${reasons}`;
}

export function describeRecurrence(item: RecurringPlannedExpenseDto): string {
  const frequency = item.frequency === 'Monthly' ? '每月' : `每年 ${item.months.join('、')} 月`;
  const range = item.endMonth === null
    ? `${formatBudgetMonth(item.startMonth)} 起`
    : `${formatBudgetMonth(item.startMonth)}–${formatBudgetMonth(item.endMonth)}`;
  return `${frequency}，${range}`;
}

// <input type="month"> 的值是 yyyy-MM；空字串代表未填
export function monthInputToKey(value: string): number | null {
  if (!/^\d{4}-\d{2}$/.test(value)) return null;
  return Number(value.slice(0, 4)) * 100 + Number(value.slice(5, 7));
}

export function keyToMonthInput(key: number | null): string {
  return key === null ? '' : `${Math.floor(key / 100)}-${String(key % 100).padStart(2, '0')}`;
}
