import { expect, test } from '@playwright/test';
import { BOOK_ID, mockApi } from './fixtures';

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

  await page.getByRole('button', { name: '飲食 更多操作', exact: true }).click();
  await page.getByRole('menuitem', { name: '設定本月預算', exact: true }).click();
  await page.getByRole('textbox', { name: '飲食 本月預算', exact: true }).fill('1,000');
  await page.getByRole('button', { name: '儲存', exact: true }).click();

  await expect(row.locator('.source')).toHaveText('本月');
  await expect(row.locator('.remaining')).toHaveText('-200');
  await expect(row).toHaveClass(/over-budget/);
  expect(putTokens).toEqual(['e2e-token']);
  expect(putBodies).toEqual([{ amount: 1000 }]);
});
