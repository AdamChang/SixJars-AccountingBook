import { Locator, expect, test } from '@playwright/test';
import { BOOK_ID, mockApi } from './fixtures';

// 進度條實際畫出來的顏色與「剩餘」的赤字紅（--red-ink）
const barColor = (row: Locator) =>
  row.locator('.mdc-linear-progress__primary-bar .mdc-linear-progress__bar-inner').evaluate(el => getComputedStyle(el).borderTopColor);
const redInk = (row: Locator) => row.locator('.remaining').evaluate(el => getComputedStyle(el).color);

const SHEET = {
  rows: [{ categoryId: 'cat-food', defaultAmount: 5000, budget: 5000, source: 'Default', actual: 1200, remaining: 3800 }],
  totals: { budget: 5000, actual: 1200, remaining: 3800 },
};

// 用真實瀏覽器走一次主要流程：lazy 路由、行內編輯（文字框、千分位）、PUT 的 XSRF header 與 body、以回傳值更新該列
test('budgets_set_month_override_inline', async ({ page }) => {
  await mockApi(page);
  const putTokens: (string | undefined)[] = [];
  const putBodies: unknown[] = [];

  await page.route(`**/api/books/${BOOK_ID}/budgets?*`, route => route.fulfill({ json: SHEET }));
  await page.route(`**/api/books/${BOOK_ID}/budgets/cat-food/overrides/202604`, async route => {
    putTokens.push(route.request().headers()['x-xsrf-token']);
    putBodies.push(route.request().postDataJSON());
    await route.fulfill({ json: { categoryId: 'cat-food', defaultAmount: 5000, overrides: [{ budgetMonth: 202604, amount: 1000 }] } });
  });

  await page.goto(`/books/${BOOK_ID}/budgets?month=202604`);
  const row = page.locator('[data-row="cat-food"]');
  await expect(row.locator('.name')).toHaveText('飲食');
  await expect(row.locator('.remaining')).toHaveText('3,800');
  const normalBar = await barColor(row);

  await page.getByRole('button', { name: '飲食 更多操作', exact: true }).click();
  await page.getByRole('menuitem', { name: '設定本月預算', exact: true }).click();
  // 選單關閉後焦點要在文字框上，直接打字、按 Enter 送出（手動驗證發現焦點回到 ⋮）
  await expect(page.getByRole('textbox', { name: '飲食 本月預算', exact: true })).toBeFocused();
  await page.keyboard.type('1,000');
  await page.keyboard.press('Enter');

  await expect(row.locator('.source')).toHaveText('本月');
  await expect(row.locator('.remaining')).toHaveText('-200');
  await expect(row).toHaveClass(/over-budget/);
  // 手動驗證發現 color="warn" 在這個 M3 主題沒有效果，超支時進度條仍是一般色
  expect(await barColor(row)).toBe(await redInk(row));
  expect(await barColor(row)).not.toBe(normalBar);
  expect(putTokens).toEqual(['e2e-token']);
  expect(putBodies).toEqual([{ amount: 1000 }]);
});

// 只用鍵盤：Tab 到 ⋮、Enter 開選單、Enter 選項目，焦點要落在文字框，Esc 取消
test('budgets_keyboard_edit_focuses_amount_box', async ({ page }) => {
  await mockApi(page);
  await page.route(`**/api/books/${BOOK_ID}/budgets?*`, route => route.fulfill({ json: SHEET }));

  await page.goto(`/books/${BOOK_ID}/budgets?month=202604`);
  await page.getByRole('button', { name: '飲食 更多操作', exact: true }).focus();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('menuitem', { name: '設定本月預算', exact: true })).toBeFocused();
  await page.keyboard.press('Enter');
  const amount = page.getByRole('textbox', { name: '飲食 本月預算', exact: true });
  await expect(amount).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(amount).toHaveCount(0);
});

// 手機寬度：spec 不做手機專屬版面，但不能壞掉（計畫手動驗證第 8 項）。手動驗證發現行內編輯打開後頁面被撐寬
test.describe('narrow viewport', () => {
  test.use({ viewport: { width: 375, height: 812 } });

  test('budgets_inline_edit_does_not_overflow_on_phone_width', async ({ page }) => {
    await mockApi(page);
    await page.route(`**/api/books/${BOOK_ID}/budgets?*`, route => route.fulfill({ json: SHEET }));
    const overflow = () => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);

    await page.goto(`/books/${BOOK_ID}/budgets?month=202604`);
    await expect(page.locator('[data-row="cat-food"] .name')).toHaveText('飲食');
    expect(await overflow()).toBe(0);

    await page.getByRole('button', { name: '飲食 更多操作', exact: true }).click();
    await page.getByRole('menuitem', { name: '設定本月預算', exact: true }).click();
    await expect(page.getByRole('textbox', { name: '飲食 本月預算', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: '儲存', exact: true })).toBeVisible();
    expect(await overflow()).toBe(0);
  });
});
