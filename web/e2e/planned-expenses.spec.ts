import { expect, test } from '@playwright/test';
import { BOOK, BOOK_ID, mockApi } from './fixtures';

// 共用的 BOOK 只有浮動與收入分類；這裡回傳加上固定分類的帳本，不修改共用 fixture
const BOOK_WITH_FIXED = {
  ...BOOK,
  categories: [
    ...BOOK.categories,
    { id: 'cat-ins', name: '保險費', kind: 'Expense', nature: 'Fixed', parentId: null, sortOrder: 1, archivedAt: null },
  ],
};

const GENERATED = {
  id: 'p1', budgetMonth: 202604, categoryId: 'cat-ins', accountId: 'acc-cash', estimatedAmount: -1200, note: null,
  paidTransactionId: null, isPaid: false, version: 7, sourceId: 'r1',
};

// 用真實瀏覽器走一次主要流程：lazy 路由、產生（XSRF header）、snackbar、付款對話框送出的 body
test('planned_expenses_generate_then_pay', async ({ page }) => {
  await mockApi(page, { book: route => route.fulfill({ json: BOOK_WITH_FIXED }) });
  let generated = false;
  const generateTokens: (string | undefined)[] = [];
  const payBodies: Record<string, unknown>[] = [];

  await page.route(`**/api/books/${BOOK_ID}/planned-expenses?*`, route =>
    route.fulfill({ json: generated ? [GENERATED] : [] }));
  await page.route(`**/api/books/${BOOK_ID}/planned-expenses/generate?*`, async route => {
    generateTokens.push(route.request().headers()['x-xsrf-token']);
    generated = true;
    await route.fulfill({ json: { created: [GENERATED], skipped: [] } });
  });
  await page.route(`**/api/books/${BOOK_ID}/planned-expenses/p1/pay`, async route => {
    payBodies.push(route.request().postDataJSON() as Record<string, unknown>);
    await route.fulfill({ json: {} });
  });

  await page.goto(`/books/${BOOK_ID}/planned-expenses?month=202604`);
  await expect(page.getByRole('tab', { name: '本月', exact: true })).toBeVisible();
  await expect(page.getByText('本月沒有預定支出')).toBeVisible();

  await page.getByRole('button', { name: '產生本月', exact: true }).click();
  await expect(page.getByText('建立 1 筆')).toBeVisible();
  expect(generateTokens).toEqual(['e2e-token']);

  await page.getByRole('button', { name: '保險費 更多操作' }).click();
  await page.getByRole('menuitem', { name: '付款', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: '付款', exact: true }).click();

  await expect.poll(() => payBodies.length).toBe(1);
  expect(payBodies[0]).toMatchObject({ version: 7, accountId: 'acc-cash', amount: -1200, loanAccountId: null, loanPrincipal: null });
});
