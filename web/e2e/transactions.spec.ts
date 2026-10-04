import { Page, expect, test } from '@playwright/test';
import { BOOK_ID, mockApi } from './fixtures';

const PAGE_URL = `/books/${BOOK_ID}/transactions?month=202601`;

// 新開的記帳頁沒有預設焦點：先用鍵盤選帳戶，之後 Tab 即可到金額
async function selectAccountWithKeyboard(page: Page): Promise<void> {
  const account = page.getByRole('combobox', { name: '帳戶' });
  await account.focus();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('option', { name: '現金' })).toBeVisible();
  await page.keyboard.press('Enter');
  await expect(account).toContainText('現金');
}

// 金額 → Tab（退款勾選）→ Tab → 分類
async function enterExpense(page: Page, amount: string): Promise<void> {
  await expect(page.getByLabel('金額')).toBeFocused();
  await page.keyboard.type(amount);
  await page.keyboard.press('Tab');
  await page.keyboard.press('Tab');
  await expect(page.getByRole('combobox', { name: '分類' })).toBeFocused();
  await page.keyboard.type('早');
  // 過濾後第一個選項要先被標為 active，Enter 才會選取它（否則會和送出競態）
  await expect(page.getByRole('option', { name: '飲食 / 早餐' })).toHaveClass(/mat-mdc-option-active/);
}

test('keyboard_only_continuous_entry', async ({ page }) => {
  // 延遲回應：若有多送的請求，會在進行中而被記錄到
  const api = await mockApi(page, { postDelayMs: 300 });
  await page.goto(PAGE_URL);
  await selectAccountWithKeyboard(page);
  await page.keyboard.press('Tab');

  await enterExpense(page, '100');
  await page.keyboard.press('Enter');             // 選取選項，不應送出
  // 面板關閉且儲存鈕仍可按（沒進入 pending）後，才斷言沒有 POST
  await expect(page.getByRole('option', { name: '飲食 / 早餐' })).toBeHidden();
  await expect(page.getByRole('combobox', { name: '分類' })).toHaveValue('飲食 / 早餐');
  await expect(page.getByRole('button', { name: '儲存' })).toBeEnabled();
  expect(api.posts).toHaveLength(0);

  await page.keyboard.press('Enter');             // 送出
  await expect(page.getByLabel('金額')).toBeFocused();   // 儲存完成後焦點回到金額
  expect(api.posts).toHaveLength(1);
  expect(api.posts[0]).toMatchObject({ kind: 'Expense', amount: -100, categoryId: 'cat-breakfast' });
  expect(api.xsrfTokens[0]).toBe('e2e-token');

  await enterExpense(page, '60');
  await page.keyboard.press('Enter');
  await expect(page.getByRole('option', { name: '飲食 / 早餐' })).toBeHidden();
  await expect(page.getByRole('button', { name: '儲存' })).toBeEnabled();
  expect(api.posts).toHaveLength(1);
  await page.keyboard.press('Enter');
  await expect(page.getByLabel('金額')).toBeFocused();
  expect(api.posts).toHaveLength(2);
  expect(api.posts[1]).toMatchObject({ amount: -60 });
});

test('does_not_post_twice_on_rapid_enter', async ({ page }) => {
  const api = await mockApi(page, { postDelayMs: 500 });
  await page.goto(PAGE_URL);
  await selectAccountWithKeyboard(page);
  await page.keyboard.press('Tab');
  await enterExpense(page, '100');
  await page.keyboard.press('Enter');
  // 請求進行中連按 Enter：不可重複記帳
  await page.keyboard.press('Enter');
  await page.keyboard.press('Enter');
  await page.keyboard.press('Enter');

  await expect(page.getByLabel('金額')).toBeFocused();
  expect(api.posts).toHaveLength(1);
});

test('shows_locked_message', async ({ page }) => {
  await mockApi(page, {
    transactions: async route => {
      if (route.request().method() === 'POST') {
        await route.fulfill({ status: 422, json: { code: 'locked', detail: '日期已鎖帳' } });
      } else {
        await route.fulfill({ json: [] });
      }
    },
  });
  await page.goto(PAGE_URL);
  await selectAccountWithKeyboard(page);
  await page.keyboard.press('Tab');
  await enterExpense(page, '100');
  await page.keyboard.press('Enter');
  await page.keyboard.press('Enter');

  await expect(page.getByText('此日期已鎖帳，不能新增、修改或刪除')).toBeVisible();
});
