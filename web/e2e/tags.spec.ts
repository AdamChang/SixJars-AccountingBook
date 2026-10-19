import { Page, Route, expect, test } from '@playwright/test';
import { BOOK, BOOK_ID, mockApi } from './fixtures';

const TRIP = { id: 'tag-trip', name: '旅遊', color: 'Teal', sortOrder: 0, archivedAt: null };
const BOOK_WITH_TAG = { ...BOOK, tags: [TRIP] };
const TAGGED = {
  id: 'tx-9', kind: 'Expense', date: '2026-01-05', budgetMonth: 202601, amount: -12000, accountId: 'acc-cash',
  counterAccountId: null, categoryId: 'cat-breakfast', planningFundId: null, loanPrincipal: null, loanInterest: null,
  note: '機票', postings: [], version: 1, tagIds: ['tag-trip'],
};

// GET 一律回掛著標籤的那筆，並記下查詢字串；POST 原樣回成 201
async function routeTransactions(page: Page, gets: URL[], posts: Record<string, unknown>[]): Promise<void> {
  await mockApi(page, {
    book: route => route.fulfill({ json: BOOK_WITH_TAG }),
    transactions: async (route: Route) => {
      const request = route.request();
      if (request.method() === 'GET') {
        gets.push(new URL(request.url()));
        await route.fulfill({ json: [TAGGED] });
        return;
      }
      const input = request.postDataJSON() as Record<string, unknown>;
      posts.push(input);
      await route.fulfill({ status: 201, json: { ...TAGGED, ...input, id: 'tx-10' } });
    },
  });
}

test('settings_adds_tag_with_chosen_color', async ({ page }) => {
  await mockApi(page);
  const bodies: unknown[] = [];
  await page.route(`**/api/books/${BOOK_ID}/tags`, async route => {
    bodies.push(route.request().postDataJSON());
    await route.fulfill({ status: 201, json: { id: 'tag-new' } });
  });

  await page.goto(`/books/${BOOK_ID}/settings`);
  await page.getByRole('tab', { name: '標籤', exact: true }).click();
  await page.locator('[data-group="tags"] button.add').click();
  await page.getByRole('textbox', { name: '名稱' }).fill('旅遊');
  await page.getByRole('radio', { name: '藍綠', exact: true }).check();
  await page.getByRole('button', { name: '儲存' }).click();

  await expect.poll(() => bodies.length).toBe(1);
  expect(bodies[0]).toEqual({ name: '旅遊', color: 'Teal' });
});

test('transaction_with_tag_then_filter_by_tag', async ({ page }) => {
  const gets: URL[] = [];
  const posts: Record<string, unknown>[] = [];
  await routeTransactions(page, gets, posts);
  await page.goto(`/books/${BOOK_ID}/transactions?month=202601`);

  // 清單：標籤 chip 在備註欄
  await expect(page.locator('td.mat-column-note .tag-chip')).toHaveText('旅遊');

  // 記一筆掛「旅遊」的支出
  await page.getByRole('combobox', { name: '帳戶' }).click();
  await page.getByRole('option', { name: '現金' }).click();
  await page.getByLabel('金額').fill('300');
  await page.getByRole('combobox', { name: '分類' }).fill('早');
  await page.getByRole('option', { name: '飲食 / 早餐' }).click();
  await page.getByRole('combobox', { name: '標籤', exact: true }).click();
  await page.getByRole('option', { name: '旅遊' }).click();
  await page.keyboard.press('Escape');
  await page.getByRole('button', { name: '儲存' }).click();
  await expect.poll(() => posts.length).toBe(1);
  expect(posts[0]).toMatchObject({ kind: 'Expense', amount: -300, tagIds: ['tag-trip'] });

  // 依標籤篩選：URL 帶 tag，查詢帶 tagId
  await page.getByRole('combobox', { name: '標籤篩選' }).click();
  await page.getByRole('option', { name: '旅遊' }).click();
  await expect(page).toHaveURL(/[?&]tag=tag-trip/);
  await expect.poll(() => gets.at(-1)?.searchParams.get('tagId')).toBe('tag-trip');
  expect(gets.at(-1)?.searchParams.get('budgetMonth')).toBe('202601');
});

// 手機寬度：標籤 chip 與篩選下拉不能把頁面撐寬
test.describe('narrow viewport', () => {
  test.use({ viewport: { width: 375, height: 812 } });

  test('transactions_and_tag_settings_do_not_overflow_on_phone_width', async ({ page }) => {
    await routeTransactions(page, [], []);
    const overflow = () => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);

    await page.goto(`/books/${BOOK_ID}/transactions?month=202601&tag=tag-trip`);
    await expect(page.locator('td.mat-column-note .tag-chip')).toHaveText('旅遊');
    expect(await overflow()).toBe(0);

    await page.goto(`/books/${BOOK_ID}/settings`);
    await page.getByRole('tab', { name: '標籤', exact: true }).click();
    await expect(page.locator('[data-group="tags"] .label')).toHaveText('旅遊');
    expect(await overflow()).toBe(0);
  });
});
