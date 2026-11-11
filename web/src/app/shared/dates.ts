const pad = (value: number, length = 2) => String(value).padStart(length, '0');

// 不可用 toISOString：UTC+8 在 00:00–08:00 會得到前一天
export function toDateString(date: Date): string {
  return `${pad(date.getFullYear(), 4)}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export function parseDateString(value: string): Date {
  const [year, month, day] = value.split('-').map(Number);
  return new Date(year, month - 1, day);
}

// 字串直接取前 7 碼，避免經過 Date／UTC 轉換造成跨日
export function budgetMonthOf(date: Date | string): number {
  const text = typeof date === 'string' ? date : toDateString(date);
  return Number(text.slice(0, 4)) * 100 + Number(text.slice(5, 7));
}

export function formatBudgetMonth(budgetMonth: number): string {
  return `${Math.floor(budgetMonth / 100)}/${pad(budgetMonth % 100)}`;
}

// 該月最後一天（yyyy-MM-dd）；Date 的日為 0 即上個月最後一天
export function lastDayOfBudgetMonth(budgetMonth: number): string {
  return toDateString(new Date(Math.floor(budgetMonth / 100), budgetMonth % 100, 0));
}

export function addMonths(budgetMonth: number, delta: number): number {
  const index = Math.floor(budgetMonth / 100) * 12 + (budgetMonth % 100 - 1) + delta;
  return Math.floor(index / 12) * 100 + (index % 12) + 1;
}

// 與後端 BudgetMonth.MinYear／MaxYear 對齊；超出範圍後端一律回 400
export const BUDGET_MONTH_MIN_YEAR = 1900;
export const BUDGET_MONTH_MAX_YEAR = 2999;

export function parseBudgetMonth(value: string | null): number | null {
  if (value === null || !/^\d{6}$/.test(value)) {
    return null;
  }
  const year = Number(value.slice(0, 4));
  const month = Number(value.slice(4));
  const yearInRange = year >= BUDGET_MONTH_MIN_YEAR && year <= BUDGET_MONTH_MAX_YEAR;
  return yearInRange && month >= 1 && month <= 12 ? Number(value) : null;
}
