import { Page, expect, test } from '@playwright/test';
import { BOOK_ID, mockApi } from './fixtures';

// 年報 16 欄在電腦寬度一個畫面放得下，手機寬度在表格容器內橫向捲動（2026-10-23 手動驗證的需求）。
// 金額取比一般家庭帳本更長的位數，真實資料只會更短。
const MONTH = 123456.78;
const wide = (share: number | null) => ({
  months: Array.from({ length: 12 }, () => MONTH), total: MONTH * 12, shareOfIncome: share, monthlyAverage: MONTH,
});
const line = (id: string, name: string, children: unknown[] = []) =>
  ({ kind: 'Category', id, name, figures: wide(0.1234), children });
const YEARLY = {
  year: 2026, averageDivisor: 12, averageFromMonth: 1,
  income: { kind: 'Income', figures: wide(1), lines: [line('cat-salary', '工作薪資')] },
  expenses: ['Floating', 'Fixed', 'Loan', 'Special'].map(kind => ({
    kind, figures: wide(0.1234),
    lines: kind === 'Floating' ? [line('cat-finance', '金融交易', [line('cat-fee', '手續費')])] : [],
  })),
  expenseTotal: wide(0.4936),
  planning: { allocation: [line('fund-1', '財務自由帳戶')], withdrawal: [], return: [] },
};

async function openYearly(page: Page) {
  await mockApi(page);
  await page.route(`**/api/books/${BOOK_ID}/reports/yearly?*`, route => route.fulfill({ json: YEARLY }));
  await page.goto(`/books/${BOOK_ID}/reports/yearly?month=202604`);
  await expect(page.locator('thead th')).toHaveCount(16);
}

// 容器的內容寬度超出可視寬度時才需要捲動
const overflowsHorizontally = (page: Page) =>
  page.locator('.table-wrap').evaluate((wrap: HTMLElement) => wrap.scrollWidth > wrap.clientWidth + 1);

test('yearly_table_fits_without_scrolling_on_desktop_width', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await openYearly(page);

  expect(await overflowsHorizontally(page)).toBe(false);
  const last = await page.locator('thead th').last().boundingBox();
  expect(last!.x + last!.width).toBeLessThanOrEqual(1280);
  await expect(page.locator('thead th').last()).toHaveText('月平均');
});

// 1024–1439px 用 12px 的數字才放得下；1440px 以上空間足夠，換回較大的字（13px）
const amountFontSize = (page: Page) =>
  page.locator('td.amount').first().evaluate((td: HTMLElement) => getComputedStyle(td).fontSize);

test('yearly_table_uses_compact_figures_on_narrow_desktop_width', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await openYearly(page);

  expect(await amountFontSize(page)).toBe('12px');
});

test('yearly_table_uses_larger_figures_and_still_fits_on_wide_desktop_width', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await openYearly(page);

  expect(await amountFontSize(page)).toBe('13px');
  expect(await overflowsHorizontally(page)).toBe(false);
  const last = await page.locator('thead th').last().boundingBox();
  expect(last!.x + last!.width).toBeLessThanOrEqual(1440);
});

test('yearly_table_scrolls_inside_its_container_on_phone_width', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  await openYearly(page);

  expect(await overflowsHorizontally(page)).toBe(true);
  // 只有表格容器捲動，整個頁面不能被撐寬
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(375);
});
