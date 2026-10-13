import { expect, test } from '@playwright/test';
import { BOOK_ID, mockApi } from './fixtures';

const line = (kind: string, id: string, name: string, amount: number, children: unknown[] = []) =>
  ({ kind, id, name, amount, principal: null, interest: null, children });
const MONTHLY = {
  budgetMonth: 202604, monthlyDisposable: 12345,
  income: { kind: 'Income', total: 50000, unpaid: null, lines: [line('Category', 'cat-salary', '薪資', 50000)] },
  expenses: [
    { kind: 'Floating', total: 150, unpaid: null, lines: [line('Category', 'cat-food', '飲食', 150, [line('Category', 'cat-breakfast', '早餐', 150)])] },
    { kind: 'Fixed', total: 1000, unpaid: 0, lines: [] },
    { kind: 'Loan', total: 0, unpaid: 0, lines: [] },
    { kind: 'Special', total: 0, unpaid: 0, lines: [] },
  ],
  expenseTotal: 1150, unpaidTotal: 0, planning: [],
};
const figures = (january: number) => ({ months: Array.from({ length: 12 }, (_, i) => (i === 0 ? january : 0)), total: january, shareOfIncome: null, monthlyAverage: null });
const YEARLY = {
  year: 2026, averageDivisor: 4, averageFromMonth: 1,
  income: { kind: 'Income', figures: figures(50000), lines: [] },
  expenses: ['Floating', 'Fixed', 'Loan', 'Special'].map(kind => ({ kind, figures: figures(kind === 'Floating' ? 150 : 0), lines: [] })),
  expenseTotal: figures(150), planning: { allocation: [], withdrawal: [], return: [] },
};

// canvas 有沒有被畫過：任一像素的 alpha 不為 0
const painted = (selector: string) => (page: import('@playwright/test').Page) =>
  page.locator(selector).first().evaluate((canvas: HTMLCanvasElement) => {
    const { data } = canvas.getContext('2d')!.getImageData(0, 0, canvas.width, canvas.height);
    return data.some((value, i) => i % 4 === 3 && value !== 0);
  });

test('reports_monthly_then_yearly', async ({ page }) => {
  await mockApi(page);
  const yearlyRequests: string[] = [];
  await page.route(`**/api/books/${BOOK_ID}/reports/monthly?*`, route => route.fulfill({ json: MONTHLY }));
  await page.route(`**/api/books/${BOOK_ID}/reports/yearly?*`, route => {
    yearlyRequests.push(route.request().url());
    return route.fulfill({ json: YEARLY });
  });

  await page.goto(`/books/${BOOK_ID}/reports?month=202604`);
  await expect(page).toHaveURL(new RegExp(`/books/${BOOK_ID}/reports/monthly\\?month=202604$`));
  await expect(page.locator('.disposable')).toContainText('12,345');
  await page.getByRole('button', { name: '展開 飲食', exact: true }).click();
  await expect(page.locator('[data-line="Category:cat-breakfast"]')).toContainText('早餐');
  await expect(page.locator('app-chart canvas')).toHaveCount(2);
  await expect.poll(() => painted('app-chart canvas')(page)).toBe(true);

  await page.getByRole('link', { name: '年報', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/books/${BOOK_ID}/reports/yearly\\?month=202604$`));
  await expect(page.locator('thead th')).toHaveCount(16);
  await expect(page.locator('app-chart canvas')).toHaveCount(1);
  await expect.poll(() => painted('app-chart canvas')(page)).toBe(true);
  expect(yearlyRequests.at(-1)).toContain('year=2026');
});
