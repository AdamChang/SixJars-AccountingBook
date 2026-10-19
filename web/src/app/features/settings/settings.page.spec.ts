import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MatButtonHarness } from '@angular/material/button/testing';
import { MatDialogHarness } from '@angular/material/dialog/testing';
import { MatInputHarness } from '@angular/material/input/testing';
import { MatMenuHarness } from '@angular/material/menu/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { MatTabGroupHarness } from '@angular/material/tabs/testing';
import { BrowserLocation } from '../../core/browser-location';
import { CurrentBook } from '../../core/book/current-book';
import { apiErrorInterceptor } from '../../core/errors/error-interceptor';
import { Notifier } from '../../core/errors/notifier';
import { BOOK } from '../transactions/testing/book-fixture';
import { SettingsPage } from './settings.page';

async function setup() {
  const notifier = { show: vi.fn() };
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(withInterceptors([apiErrorInterceptor])), provideHttpClientTesting(),
      { provide: Notifier, useValue: notifier },
      { provide: BrowserLocation, useValue: { assign: vi.fn(), currentPath: () => '/' } },
    ],
  });
  const httpTesting = TestBed.inject(HttpTestingController);
  const loaded = TestBed.inject(CurrentBook).load(BOOK.id);
  httpTesting.expectOne(`/api/books/${BOOK.id}`).flush(BOOK);
  await loaded;
  const fixture = TestBed.createComponent(SettingsPage);
  await fixture.whenStable();
  const loader = TestbedHarnessEnvironment.loader(fixture);
  const rootLoader = TestbedHarnessEnvironment.documentRootLoader(fixture);
  const el = fixture.nativeElement as HTMLElement;
  // 分頁切換有轉場，新分頁的內容在轉場結束後才掛上 DOM；等到 ready 出現再繼續
  const openTab = async (label: string, ready: string) => {
    await (await loader.getHarness(MatTabGroupHarness)).selectTab({ label });
    await vi.waitFor(() => expect(el.querySelector(`.mat-mdc-tab-body-active ${ready}`)).not.toBeNull());
    await fixture.whenStable();
  };
  const rowLabels = (selector: string) =>
    [...el.querySelectorAll(`${selector} .active .label`)].map(n => n.textContent!.trim());
  // 寫入成功後頁面會重新載入帳本
  const expectReload = async () => {
    await fixture.whenStable();
    httpTesting.expectOne(`/api/books/${BOOK.id}`).flush(BOOK);
    await fixture.whenStable();
  };
  return { fixture, loader, rootLoader, el, httpTesting, notifier, openTab, rowLabels, expectReload };
}

const ACCOUNTS_ORDER = { method: 'PUT', url: `/api/books/${BOOK.id}/accounts/order` };

describe('SettingsPage', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('lists_category_groups_in_sort_order', async () => {
    const { rowLabels } = await setup();
    expect(rowLabels('[data-group="main-Expense"]')).toEqual(['飲食']);
    expect(rowLabels('[data-group="main-Income"]')).toEqual(['薪資']);
    expect(rowLabels('[data-group="sub-cat-food"]')).toEqual(['午餐']);
  });

  it('moving_account_sends_order_and_reloads_book', async () => {
    const { el, fixture, httpTesting, openTab, notifier, expectReload } = await setup();
    await openTab('帳戶', '[data-group="accounts"]');
    el.querySelectorAll<HTMLButtonElement>('[data-group="accounts"] .active button.up')[1].click();
    await fixture.whenStable();

    const request = httpTesting.expectOne(ACCOUNTS_ORDER);
    expect(request.request.body).toEqual({ ids: ['acc-bank', 'acc-cash', 'acc-card', 'acc-ewallet', 'acc-loan'] });
    request.flush(null, { status: 204, statusText: '' });
    await expectReload();
    expect(notifier.show).not.toHaveBeenCalled();   // 排序成功不打擾
  });

  it('failed_reorder_reverts_and_shows_backend_message', async () => {
    const { el, fixture, httpTesting, openTab, notifier, rowLabels } = await setup();
    await openTab('帳戶', '[data-group="accounts"]');
    el.querySelectorAll<HTMLButtonElement>('[data-group="accounts"] .active button.up')[1].click();
    await fixture.whenStable();

    httpTesting.expectOne(ACCOUNTS_ORDER).flush(
      { detail: '帳戶的排序清單與目前的項目不一致，請重新載入後再試。', code: 'rule' },
      { status: 422, statusText: 'Unprocessable Entity' });
    await fixture.whenStable();

    expect(rowLabels('[data-group="accounts"]')).toEqual(['現金', '銀行', '信用卡', '悠遊卡', '房貸']);
    expect(notifier.show).toHaveBeenCalledWith('帳戶的排序清單與目前的項目不一致，請重新載入後再試。');
  });

  it('archive_from_menu_posts_and_reloads', async () => {
    const { loader, httpTesting, openTab, notifier, expectReload } = await setup();
    await openTab('帳戶', '[data-group="accounts"]');
    await (await loader.getHarness(MatMenuHarness.with({ selector: '[data-menu="acc-card"]' }))).clickItem({ text: '封存' });

    httpTesting.expectOne({ method: 'POST', url: `/api/books/${BOOK.id}/accounts/acc-card/archive` })
      .flush(null, { status: 204, statusText: '' });
    await expectReload();
    expect(notifier.show).toHaveBeenCalledWith('已封存「信用卡」');
  });

  it('remove_asks_for_confirmation_then_deletes', async () => {
    const { fixture, loader, rootLoader, httpTesting, notifier, expectReload } = await setup();
    await (await loader.getHarness(MatMenuHarness.with({ selector: '[data-menu="cat-lunch"]' }))).clickItem({ text: '刪除' });
    const dialog = await rootLoader.getHarness(MatDialogHarness);
    expect(await dialog.getText()).toContain('確定要刪除「午餐」？');
    await (await rootLoader.getHarness(MatButtonHarness.with({ text: '刪除' }))).click();
    await fixture.whenStable();

    // 確認框關閉動畫結束後才送出
    await vi.waitFor(() => httpTesting.expectOne({ method: 'DELETE', url: `/api/books/${BOOK.id}/categories/cat-lunch` })
      .flush(null, { status: 204, statusText: '' }));
    await expectReload();
    expect(notifier.show).toHaveBeenCalledWith('已刪除「午餐」');
  });

  it('edit_expense_main_category_puts_name_and_nature', async () => {
    const { fixture, loader, rootLoader, httpTesting, expectReload } = await setup();
    await (await loader.getHarness(MatMenuHarness.with({ selector: '[data-menu="cat-food"]' }))).clickItem({ text: '修改' });
    await (await rootLoader.getHarness(MatInputHarness.with({ selector: '[formControlName=name]' }))).setValue('餐飲');
    const nature = await rootLoader.getHarness(MatSelectHarness.with({ selector: '[formControlName=nature]' }));
    await nature.open();
    await nature.clickOptions({ text: '特別' });
    await (await rootLoader.getHarness(MatButtonHarness.with({ text: '儲存' }))).click();
    await fixture.whenStable();

    await vi.waitFor(() => {
      const request = httpTesting.expectOne({ method: 'PUT', url: `/api/books/${BOOK.id}/categories/cat-food` });
      expect(request.request.body).toEqual({ name: '餐飲', nature: 'Special' });
      request.flush(null, { status: 204, statusText: '' });
    });
    await expectReload();
  });

  it('tags_tab_sits_between_funds_and_lock_date_and_lists_tags_in_sort_order', async () => {
    const { loader, openTab, rowLabels, el } = await setup();
    const tabs = await (await loader.getHarness(MatTabGroupHarness)).getTabs();
    expect(await Promise.all(tabs.map(t => t.getLabel()))).toEqual(['分類', '帳戶', '財務規劃帳戶', '標籤', '鎖帳日與資料']);
    await openTab('標籤', '[data-group="tags"]');
    expect(rowLabels('[data-group="tags"]')).toEqual(['工作', '旅遊']);
    expect(el.querySelector('[data-group="tags"] .archived, [data-group="tags"] .toggle-archived')).not.toBeNull();
  });

  it('add_tag_defaults_to_first_unused_color_and_posts', async () => {
    const { fixture, el, openTab, rootLoader, httpTesting, notifier, expectReload } = await setup();
    await openTab('標籤', '[data-group="tags"]');
    el.querySelector<HTMLButtonElement>('[data-group="tags"] button.add')!.click();
    await fixture.whenStable();
    // BOOK 的標籤用了 Blue、Teal、Purple，第一個沒用過的是 Red
    await vi.waitFor(() => expect(document.querySelector<HTMLInputElement>('[role=radiogroup] input[aria-label="紅"]')?.checked).toBe(true));
    await (await rootLoader.getHarness(MatInputHarness.with({ selector: '[formControlName=name]' }))).setValue('可報帳');
    await (await rootLoader.getHarness(MatButtonHarness.with({ text: '儲存' }))).click();
    await fixture.whenStable();

    await vi.waitFor(() => {
      const request = httpTesting.expectOne({ method: 'POST', url: `/api/books/${BOOK.id}/tags` });
      expect(request.request.body).toEqual({ name: '可報帳', color: 'Red' });
      request.flush({ id: 'tag-new' }, { status: 201, statusText: '' });
    });
    await expectReload();
    expect(notifier.show).toHaveBeenCalledWith('已新增「可報帳」');
  });

  it('edit_tag_puts_name_and_color', async () => {
    const { fixture, loader, rootLoader, openTab, httpTesting, expectReload } = await setup();
    await openTab('標籤', '[data-group="tags"]');
    await (await loader.getHarness(MatMenuHarness.with({ selector: '[data-menu="tag-trip"]' }))).clickItem({ text: '修改' });
    await (await rootLoader.getHarness(MatInputHarness.with({ selector: '[formControlName=name]' }))).setValue('日本旅遊');
    document.querySelector<HTMLInputElement>('[role=radiogroup] input[aria-label="粉紅"]')!.click();
    await (await rootLoader.getHarness(MatButtonHarness.with({ text: '儲存' }))).click();
    await fixture.whenStable();

    await vi.waitFor(() => {
      const request = httpTesting.expectOne({ method: 'PUT', url: `/api/books/${BOOK.id}/tags/tag-trip` });
      expect(request.request.body).toEqual({ name: '日本旅遊', color: 'Pink' });
      request.flush(null, { status: 204, statusText: '' });
    });
    await expectReload();
  });

  it('tag_archive_reorder_and_in_use_removal_use_tags_path', async () => {
    const { el, fixture, loader, rootLoader, openTab, httpTesting, notifier, expectReload } = await setup();
    await openTab('標籤', '[data-group="tags"]');
    await (await loader.getHarness(MatMenuHarness.with({ selector: '[data-menu="tag-trip"]' }))).clickItem({ text: '封存' });
    httpTesting.expectOne({ method: 'POST', url: `/api/books/${BOOK.id}/tags/tag-trip/archive` }).flush(null, { status: 204, statusText: '' });
    await expectReload();

    el.querySelectorAll<HTMLButtonElement>('[data-group="tags"] .active button.up')[1].click();
    await fixture.whenStable();
    const order = httpTesting.expectOne({ method: 'PUT', url: `/api/books/${BOOK.id}/tags/order` });
    expect(order.request.body).toEqual({ ids: ['tag-trip', 'tag-work', 'tag-old'] });
    order.flush(null, { status: 204, statusText: '' });
    await expectReload();

    await (await loader.getHarness(MatMenuHarness.with({ selector: '[data-menu="tag-work"]' }))).clickItem({ text: '刪除' });
    await (await rootLoader.getHarness(MatButtonHarness.with({ text: '刪除' }))).click();
    await fixture.whenStable();
    await vi.waitFor(() => httpTesting.expectOne({ method: 'DELETE', url: `/api/books/${BOOK.id}/tags/tag-work` }).flush(
      { detail: '「工作」已有交易或預定支出使用，不能刪除；可以改用封存。', code: 'in-use' },
      { status: 422, statusText: 'Unprocessable Entity' }));
    await fixture.whenStable();
    expect(notifier.show).toHaveBeenCalledWith('「工作」已有交易或預定支出使用，不能刪除；可以改用封存。');
  });

  it('saves_lock_date_and_offers_downloads', async () => {
    const { el, fixture, httpTesting, openTab, expectReload } = await setup();
    await openTab('鎖帳日與資料', 'input[type=date]');
    const input = el.querySelector<HTMLInputElement>('input[type=date]')!;
    input.value = '2026-03-31';
    input.dispatchEvent(new Event('input'));
    el.querySelector<HTMLButtonElement>('button.save-lock-date')!.click();
    await fixture.whenStable();

    const request = httpTesting.expectOne({ method: 'PUT', url: `/api/books/${BOOK.id}/lock-date` });
    expect(request.request.body).toEqual({ lockDate: '2026-03-31' });
    request.flush(null, { status: 204, statusText: '' });
    await expectReload();

    const links = [...el.querySelectorAll<HTMLAnchorElement>('a[download]')].map(a => a.getAttribute('href'));
    expect(links).toEqual([
      `/api/books/${BOOK.id}/export/transactions.csv`,
      `/api/books/${BOOK.id}/export/transactions.xlsx`,
      `/api/books/${BOOK.id}/export/backup.json`,
    ]);
  });

  const PREVIEW = {
    name: BOOK.name, openingDate: '2026-01-01', lockDate: null, transactions: 0, plannedExpenses: 0,
    recurringPlannedExpenses: 0, categoryBudgets: 0, members: 1, auditEntries: 0,
  };

  // 開啟刪除對話框、輸入名稱並按「永久刪除」，回傳送出的 DELETE 請求
  async function confirmDelete(ctx: Awaited<ReturnType<typeof setup>>) {
    await ctx.openTab('鎖帳日與資料', '.delete-book');
    ctx.el.querySelector<HTMLButtonElement>('.delete-book button')!.click();
    await ctx.fixture.whenStable();
    ctx.httpTesting.expectOne(`/api/books/${BOOK.id}/deletion-preview`).flush(PREVIEW);
    await ctx.fixture.whenStable();
    await (await ctx.rootLoader.getHarness(MatInputHarness.with({ selector: '.confirm-name input' }))).setValue(BOOK.name);
    await (await ctx.rootLoader.getHarness(MatButtonHarness.with({ text: '永久刪除' }))).click();
    // afterClosed 在對話框的關閉動畫結束後才發出，DELETE 會比 whenStable 晚送出
    return vi.waitFor(() => ctx.httpTesting.expectOne({ method: 'DELETE', url: `/api/books/${BOOK.id}` }));
  }

  // 真實的 navigate 在新路由啟用後才 resolve，這一頁已經被銷毀；只 mock 成 true 會讓頁面在 CurrentBook.clear() 後還在跑變更偵測。
  // 導頁期間舊頁面仍會重新渲染，所以先等變更偵測跑完再銷毀：clear() 若搬到導頁之前，這裡就會讀到 undefined 的 book
  function leaveOnNavigate(ctx: Awaited<ReturnType<typeof setup>>) {
    return vi.spyOn(TestBed.inject(Router), 'navigate').mockImplementation(async () => {
      await ctx.fixture.whenStable();
      ctx.fixture.destroy();
      return true;
    });
  }

  async function expectMeReload(httpTesting: HttpTestingController) {
    await vi.waitFor(() => httpTesting.expectOne('/api/me').flush({ subject: 's', email: null, books: [] }));
    await vi.waitFor(() => httpTesting.expectOne('/api/antiforgery/token').flush(null));
  }

  it('delete_book_reloads_session_then_goes_home_and_clears_current_book', async () => {
    const ctx = await setup();
    const navigate = leaveOnNavigate(ctx);

    const request = await confirmDelete(ctx);
    expect(request.request.body).toEqual({ confirmName: BOOK.name });
    request.flush(null, { status: 204, statusText: '' });
    await expectMeReload(ctx.httpTesting);

    await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith(['/']));
    await vi.waitFor(() => expect(TestBed.inject(CurrentBook).book()).toBeUndefined());
    expect(ctx.notifier.show).toHaveBeenCalledWith(`已刪除帳本「${BOOK.name}」`);
  });

  it('delete_book_domain_error_shows_backend_message_and_stays', async () => {
    const ctx = await setup();
    const navigate = leaveOnNavigate(ctx);

    (await confirmDelete(ctx)).flush(
      { code: 'name-mismatch', detail: '輸入的名稱與帳本名稱不同，沒有刪除。' }, { status: 422, statusText: '' });
    await ctx.fixture.whenStable();

    expect(ctx.notifier.show).toHaveBeenCalledWith('輸入的名稱與帳本名稱不同，沒有刪除。');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('delete_book_not_found_leaves_without_success_message', async () => {
    const ctx = await setup();
    const navigate = leaveOnNavigate(ctx);

    (await confirmDelete(ctx)).flush(null, { status: 404, statusText: '' });
    await expectMeReload(ctx.httpTesting);

    await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith(['/']));
    expect(ctx.notifier.show).not.toHaveBeenCalled();
  });
});
