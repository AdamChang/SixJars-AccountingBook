// web/src/app/features/budgets/budget-rules.ts
import { ValidatorFn } from '@angular/forms';
import { BudgetRowDto, BudgetSource, CategoryBudgetDto } from '../../core/api/dto';

// 行內編輯用文字框（inputmode="decimal"）：jsdom 會把不合法的 type="number" 值清成空字串，測不到打錯格式。
// 接受千分位逗號、最多兩位小數；負數、空白、其他字元都不合法（後端另有 >= 0 的檢查）
export function parseBudgetAmount(text: string): number | null {
  const normalized = text.trim().replaceAll(',', '');
  return /^\d+(\.\d{1,2})?$/.test(normalized) ? Number(normalized) : null;
}

export const budgetAmountValidator: ValidatorFn = control =>
  parseBudgetAmount(String(control.value ?? '')) === null ? { budgetAmount: true } : null;

export function sourceLabel(source: BudgetSource | null): string {
  switch (source) {
    case 'Override':
      return '本月';
    case 'Default':
      return '預設';
    default:
      return '未設';
  }
}

// 使用率（%），不截斷，超支時大於 100；進度條自行取 min(100)。預算 0 時有花就是 100%
export function usagePercent(row: Pick<BudgetRowDto, 'budget' | 'actual'>): number | null {
  if (row.budget === null) {
    return null;
  }
  if (row.budget === 0) {
    return row.actual > 0 ? 100 : 0;
  }
  return Math.round((row.actual / row.budget) * 100);
}

export function isOverBudget(row: Pick<BudgetRowDto, 'budget' | 'actual'>): boolean {
  return row.budget !== null && row.actual > row.budget;
}

// 以 PUT 的回傳更新該列（P4 L plan D5）。規則同後端 CategoryBudget.AmountFor：本月覆寫值優先（0 也算），其次預設值
export function applyBudget(row: BudgetRowDto, budget: CategoryBudgetDto, budgetMonth: number): BudgetRowDto {
  const monthOverride = budget.overrides.find(o => o.budgetMonth === budgetMonth);
  const amount = monthOverride?.amount ?? budget.defaultAmount;
  const source: BudgetSource | null = monthOverride ? 'Override' : budget.defaultAmount !== null ? 'Default' : null;
  return { ...row, defaultAmount: budget.defaultAmount, budget: amount, source, remaining: amount === null ? null : amount - row.actual };
}
