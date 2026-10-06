import { Page, Route } from '@playwright/test';

// e2e 是獨立的 TS program，不引用 app 程式碼；DTO 只複製必要的欄位
export const BOOK_ID = 'b1';

export const BOOK = {
  id: BOOK_ID,
  name: '我的帳本',
  openingDate: '2026-01-01',
  lockDate: null,
  accounts: [{ id: 'acc-cash', name: '現金', type: 'Cash', openingBalance: 0, countsAsAvailableCash: true, sortOrder: 0, archivedAt: null }],
  planningFunds: [],
  categories: [
    { id: 'cat-food', name: '飲食', kind: 'Expense', nature: 'Floating', parentId: null, sortOrder: 0, archivedAt: null },
    { id: 'cat-breakfast', name: '早餐', kind: 'Expense', nature: 'Floating', parentId: 'cat-food', sortOrder: 0, archivedAt: null },
    { id: 'cat-salary', name: '薪資', kind: 'Income', nature: null, parentId: null, sortOrder: 0, archivedAt: null },
  ],
};

export interface MockApiOptions {
  // 覆寫個別路由；沒給的維持預設
  me?: (route: Route) => Promise<void>;
  book?: (route: Route) => Promise<void>;
  transactions?: (route: Route) => Promise<void>;
  // POST 回應前的延遲，用來製造「請求進行中」的視窗
  postDelayMs?: number;
}

export interface MockApi {
  // 依序記錄收到的 POST /transactions request body
  posts: Record<string, unknown>[];
  // 與 posts 對應的 X-XSRF-TOKEN request header
  xsrfTokens: (string | undefined)[];
}

export async function mockApi(page: Page, options: MockApiOptions = {}): Promise<MockApi> {
  const api: MockApi = { posts: [], xsrfTokens: [] };

  await page.route('**/api/me', options.me ?? (route =>
    route.fulfill({ json: { subject: 'e2e', email: null, books: [{ id: BOOK_ID, name: BOOK.name }] } })));

  await page.route('**/api/antiforgery/token', route =>
    route.fulfill({
      status: 200,
      headers: { 'Set-Cookie': 'XSRF-TOKEN=e2e-token; Path=/' },
      body: '',
    }));

  await page.route(`**/api/books/${BOOK_ID}`, options.book ?? (route => route.fulfill({ json: BOOK })));

  await page.route(`**/api/books/${BOOK_ID}/transactions*`, options.transactions ?? (async route => {
    const request = route.request();
    if (request.method() !== 'POST') {
      await route.fulfill({ json: [] });
      return;
    }
    const input = request.postDataJSON() as Record<string, unknown>;
    api.posts.push(input);
    api.xsrfTokens.push(request.headers()['x-xsrf-token']);
    if (options.postDelayMs) {
      await new Promise(resolve => setTimeout(resolve, options.postDelayMs));
    }
    await route.fulfill({
      status: 201,
      json: {
        id: `tx-${api.posts.length}`, ...input,
        budgetMonth: 202601, loanInterest: null, postings: [], version: 1,
      },
    });
  }));

  return api;
}
