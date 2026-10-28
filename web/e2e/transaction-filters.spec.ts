import { Route, expect, test } from '@playwright/test';
import { BOOK, BOOK_ID, mockApi } from './fixtures';

const BOOK_WITH_FILTERS = {
  ...BOOK,
  accounts: [
    ...BOOK.accounts,
    { id: 'acc-bank', name: '銀行', type: 'Bank', openingBalance: 0, countsAsAvailableCash: true, sortOrder: 1, archivedAt: null },
  ],
  tags: [{ id: 'tag-trip', name: '旅遊', color: 'Teal', sortOrder: 0, archivedAt: null }],
};

// 選單 → URL → 請求 → 清除整段同步（transaction filters Q16）
test('filters_combine_then_clear_keeps_month', async ({ page }) => {
  const gets: URL[] = [];
  await mockApi(page, {
    book: route => route.fulfill({ json: BOOK_WITH_FILTERS }),
    transactions: async (route: Route) => {
      gets.push(new URL(route.request().url()));
      await route.fulfill({ json: [] });
    },
  });
  await page.goto(`/books/${BOOK_ID}/transactions?month=202601`);

  await page.getByRole('combobox', { name: '分類篩選' }).click();
  await page.getByRole('option', { name: '飲食' }).click();
  await page.getByRole('combobox', { name: '帳戶篩選' }).click();
  await page.getByRole('option', { name: '銀行' }).click();
  await page.getByRole('combobox', { name: '標籤篩選' }).click();
  await page.getByRole('option', { name: '旅遊' }).click();

  await expect(page).toHaveURL(/[?&]category=cat-food/);
  await expect(page).toHaveURL(/[?&]account=acc-bank/);
  await expect(page).toHaveURL(/[?&]tag=tag-trip/);
  await expect.poll(() => gets.at(-1)?.searchParams.toString())
    .toBe('budgetMonth=202601&tagId=tag-trip&categoryId=cat-food&accountId=acc-bank');
  await expect(page.getByText('這個月沒有符合篩選條件的交易')).toBeVisible();

  await page.getByRole('button', { name: '清除篩選' }).click();
  await expect(page).toHaveURL(new RegExp(`/books/${BOOK_ID}/transactions\\?month=202601$`));
  await expect.poll(() => gets.at(-1)?.searchParams.toString()).toBe('budgetMonth=202601');
  await expect(page.getByRole('button', { name: '清除篩選' })).toHaveCount(0);
  await expect(page.getByText('這個月還沒有交易')).toBeVisible();
});
