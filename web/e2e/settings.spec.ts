import { expect, test } from '@playwright/test';
import { BOOK, BOOK_ID, mockApi } from './fixtures';

// 用真實瀏覽器確認 settings 的 lazy 路由能載入，▲ 送出的 PUT 帶 XSRF header 與整組順序；
// drag-drop 手勢不在這裡測（元件層級已測，且與 ▲▼ 走同一條路徑，spec §8）
test('settings_reorders_account_with_up_button', async ({ page }) => {
  const bank = { ...BOOK.accounts[0], id: 'acc-bank', name: '銀行', type: 'Bank', countsAsAvailableCash: false, sortOrder: 1 };
  await mockApi(page, { book: route => route.fulfill({ json: { ...BOOK, accounts: [...BOOK.accounts, bank] } }) });
  const bodies: unknown[] = [];
  const tokens: (string | undefined)[] = [];
  await page.route(`**/api/books/${BOOK_ID}/accounts/order`, async route => {
    bodies.push(route.request().postDataJSON());
    tokens.push(route.request().headers()['x-xsrf-token']);
    await route.fulfill({ status: 204 });
  });

  await page.goto(`/books/${BOOK_ID}/settings`);
  await page.getByRole('tab', { name: '帳戶', exact: true }).click();
  await page.getByRole('button', { name: '銀行 上移' }).click();

  await expect.poll(() => bodies.length).toBe(1);
  expect(bodies[0]).toEqual({ ids: ['acc-bank', 'acc-cash'] });
  expect(tokens[0]).toBe('e2e-token');
});
