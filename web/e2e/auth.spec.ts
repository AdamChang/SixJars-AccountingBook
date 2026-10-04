import { expect, test } from '@playwright/test';
import { mockApi } from './fixtures';

test('redirects_to_backend_login_on_401', async ({ page }) => {
  await mockApi(page, { me: route => route.fulfill({ status: 401, body: '' }) });
  // 後端登入頁不在這個測試的範圍，回一段 HTML 讓導向可以完成
  await page.route('**/auth/login**', route =>
    route.fulfill({ contentType: 'text/html', body: '<!doctype html><title>login</title>' }));

  await page.goto('/books/b1/transactions?month=202601');

  await expect.poll(() => {
    const url = new URL(page.url());
    return url.pathname + url.search;
  }).toBe('/auth/login?returnUrl=%2Fbooks%2Fb1%2Ftransactions%3Fmonth%3D202601');
});
