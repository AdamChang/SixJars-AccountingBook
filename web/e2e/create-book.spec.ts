import { expect, test } from '@playwright/test';
import { BOOK, BOOK_ID, mockApi } from './fixtures';

const NEW_ID = 'b2';
const NEW_BOOK = { id: NEW_ID, name: '新帳本', openingDate: '2026-10-19', lockDate: null, accounts: [], planningFunds: [], categories: [], tags: [] };

// 從工具列的帳本選單建立：送出名稱、開帳日與 XSRF；reload me 後選單出現新帳本，並落在新帳本設定頁的帳戶分頁（spec Q21、F5）
test('toolbar_menu_creates_book_and_opens_its_accounts_tab', async ({ page }) => {
  let createBody: unknown;
  let createToken: string | undefined;
  await mockApi(page, {
    me: route => route.fulfill({
      json: {
        subject: 'e2e', email: null, canCreateBooks: true, remainingBookQuota: createBody ? 3 : 4,
        books: [{ id: BOOK_ID, name: BOOK.name }, ...(createBody ? [{ id: NEW_ID, name: NEW_BOOK.name }] : [])],
      },
    }),
  });
  await page.route('**/api/books', async route => {
    if (route.request().method() !== 'POST') {
      await route.fallback();
      return;
    }
    createBody = route.request().postDataJSON();
    createToken = route.request().headers()['x-xsrf-token'];
    await route.fulfill({ status: 201, json: { id: NEW_ID } });
  });
  await page.route(`**/api/books/${NEW_ID}`, route => route.fulfill({ json: NEW_BOOK }));

  await page.goto(`/books/${BOOK_ID}/transactions`);
  await page.getByRole('button', { name: BOOK.name }).click();
  await page.getByRole('menuitem', { name: '建立帳本…' }).click();
  await page.getByLabel('帳本名稱').fill('新帳本');
  await page.getByLabel('開帳日').fill('2026-10-19');
  await page.getByRole('button', { name: '建立', exact: true }).click();

  await expect(page).toHaveURL(/\/books\/b2\/settings\?tab=accounts$/);
  await expect(page.getByRole('tab', { name: '帳戶', exact: true })).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByText('已建立帳本「新帳本」')).toBeVisible();
  expect(createBody).toEqual({ name: '新帳本', openingDate: '2026-10-19' });
  expect(createToken).toBe('e2e-token');

  // me 已重新載入：工具列選單列出新帳本
  await page.getByRole('button', { name: NEW_BOOK.name }).click();
  await expect(page.getByRole('menuitem', { name: NEW_BOOK.name })).toBeVisible();
});
