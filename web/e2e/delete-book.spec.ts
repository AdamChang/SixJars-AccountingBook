import { expect, test } from '@playwright/test';
import { BOOK, BOOK_ID, mockApi } from './fixtures';

// 刪除後 me 不再含這本帳本：首頁必須顯示「沒有可存取的帳本」，而不是用舊的 me 自動導回已刪除的帳本（spec F3）
test('settings_deletes_book_after_typing_its_name', async ({ page }) => {
  let deleteBody: unknown;
  let deleteToken: string | undefined;
  await mockApi(page, {
    me: route => route.fulfill({
      json: { subject: 'e2e', email: null, books: deleteBody ? [] : [{ id: BOOK_ID, name: BOOK.name }] },
    }),
    book: async route => {
      if (route.request().method() === 'DELETE') {
        deleteBody = route.request().postDataJSON();
        deleteToken = route.request().headers()['x-xsrf-token'];
        await route.fulfill({ status: 204 });
        return;
      }
      await route.fulfill({ json: BOOK });
    },
  });
  await page.route(`**/api/books/${BOOK_ID}/deletion-preview`, route => route.fulfill({
    json: {
      name: BOOK.name, openingDate: '2026-01-01', lockDate: null, transactions: 3, plannedExpenses: 0,
      recurringPlannedExpenses: 0, categoryBudgets: 0, members: 1, auditEntries: 5,
    },
  }));

  await page.goto(`/books/${BOOK_ID}/settings`);
  await page.getByRole('tab', { name: '鎖帳日與資料' }).click();
  await page.getByRole('button', { name: '刪除帳本…' }).click();
  await expect(page.getByText('3 筆')).toBeVisible();
  const confirmButton = page.getByRole('button', { name: '永久刪除' });
  await expect(confirmButton).toBeDisabled();

  await page.getByLabel('輸入帳本名稱以確認').fill(BOOK.name);
  await confirmButton.click();

  await expect(page.getByText('沒有可存取的帳本')).toBeVisible();
  expect(deleteBody).toEqual({ confirmName: BOOK.name });
  expect(deleteToken).toBe('e2e-token');
});
