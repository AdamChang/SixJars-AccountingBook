import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MatButtonHarness } from '@angular/material/button/testing';
import { MatMenuHarness } from '@angular/material/menu/testing';
import { MatTabGroupHarness } from '@angular/material/tabs/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { BookDto, PlannedExpenseDto, RecurringPlannedExpenseDto } from '../../core/api/dto';
import { CurrentBook } from '../../core/book/current-book';
import { BrowserLocation } from '../../core/browser-location';
import { apiErrorInterceptor } from '../../core/errors/error-interceptor';
import { PLANNED_CONFLICT_MESSAGE } from '../../core/errors/messages';
import { Notifier } from '../../core/errors/notifier';
import { toDateString } from '../../shared/dates';
import { BOOK } from '../transactions/testing/book-fixture';
import { PlannedExpensesPage } from './planned-expenses.page';

// BOOK 的分類只有浮動與收入；補上固定與貸款
const book: BookDto = {
  ...BOOK,
  categories: [
    ...BOOK.categories,
    { id: 'ins', name: '保險費', kind: 'Expense', nature: 'Fixed', parentId: null, sortOrder: 1, archivedAt: null },
    { id: 'loan', name: '房屋貸款', kind: 'Expense', nature: 'Loan', parentId: null, sortOrder: 2, archivedAt: null },
  ],
};

const planned = (over: Partial<PlannedExpenseDto>): PlannedExpenseDto => ({
  id: 'p-fix', budgetMonth: 202604, categoryId: 'ins', accountId: 'acc-bank', estimatedAmount: -100, note: null,
  paidTransactionId: null, isPaid: false, version: 5, sourceId: null, ...over,
});

const FIXED = planned({});
const LOAN = planned({ id: 'p-loan', categoryId: 'loan', estimatedAmount: -3000, sourceId: 'r1' });

const base = `/api/books/${book.id}/planned-expenses`;
const LIST = `${base}?budgetMonth=202604`;
const GENERATE = { method: 'POST', url: `${base}/generate?budgetMonth=202604` };
const RECURRING = `/api/books/${book.id}/recurring-planned-expenses`;

const MORTGAGE: RecurringPlannedExpenseDto = {
  id: 'r1', categoryId: 'loan', accountId: 'acc-bank', defaultAmount: -3000, note: '房貸',
  frequency: 'Monthly', months: [], startMonth: 202601, endMonth: null, version: 2,
};
// 結束月份早於頁面月份（2026/04）
const ENDED: RecurringPlannedExpenseDto = {
  id: 'r2', categoryId: 'ins', accountId: null, defaultAmount: -1200, note: null,
  frequency: 'Yearly', months: [3, 9], startMonth: 202501, endMonth: 202512, version: 1,
};

async function setup(initial: PlannedExpenseDto[] = [FIXED, LOAN]) {
  const notifier = { show: vi.fn() };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([{ path: 'books/:bookId/planned-expenses', component: PlannedExpensesPage }]),
      provideHttpClient(withInterceptors([apiErrorInterceptor])),
      provideHttpClientTesting(),
      { provide: CurrentBook, useValue: { book: signal(book) } },
      { provide: Notifier, useValue: notifier },
      { provide: BrowserLocation, useValue: { assign: vi.fn(), currentPath: () => '/' } },
    ],
  });
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(`/books/${book.id}/planned-expenses?month=202604`, PlannedExpensesPage);
  const fixture = harness.fixture;
  const httpTesting = TestBed.inject(HttpTestingController);
  const loader = TestbedHarnessEnvironment.loader(fixture);
  const rootLoader = TestbedHarnessEnvironment.documentRootLoader(fixture);
  const el = fixture.nativeElement as HTMLElement;
  const flushList = async (items: PlannedExpenseDto[]) => {
    await fixture.whenStable();
    httpTesting.expectOne(LIST).flush(items);
    await fixture.whenStable();
  };
  await flushList(initial);
  const button = (selector: string) => el.querySelector<HTMLButtonElement>(selector)!;
  const click = async (selector: string) => {
    button(selector).click();
    await fixture.whenStable();
  };
  const menu = async (id: string, item: string) =>
    (await loader.getHarness(MatMenuHarness.with({ selector: `[data-menu="${id}"]` }))).clickItem({ text: item });
  // 週期項目在切到分頁時才載入；分頁內容在轉場結束後才掛上 DOM
  const openRecurring = async (items: RecurringPlannedExpenseDto[]) => {
    await (await loader.getHarness(MatTabGroupHarness)).selectTab({ label: '週期項目' });
    await vi.waitFor(() => httpTesting.expectOne(RECURRING).flush(items));
    await vi.waitFor(() => expect(el.querySelector('.mat-mdc-tab-body-active .recurring-list')).not.toBeNull());
    await fixture.whenStable();
  };
  return { fixture, httpTesting, loader, rootLoader, el, notifier, flushList, button, click, menu, openRecurring };
}

describe('PlannedExpensesPage', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('loads_month_and_groups_by_nature', async () => {
    const { el } = await setup();
    expect([...el.querySelectorAll('.group h3 .title')].map(n => n.textContent!.trim())).toEqual(['固定支出', '貸款支出']);
    expect(el.querySelector('[data-row="p-loan"] .recurring')).not.toBeNull();
    expect(el.querySelector('[data-row="p-fix"] .recurring')).toBeNull();
  });

  it('generate_shows_summary_and_reloads', async () => {
    const { click, httpTesting, notifier, flushList, fixture } = await setup([]);
    await click('button.generate');
    httpTesting.expectOne(GENERATE).flush({
      created: [FIXED], skipped: [{ recurringId: 'r2', plannedExpenseId: null, reason: 'CategoryArchived' }],
    });
    await fixture.whenStable();
    expect(notifier.show).toHaveBeenCalledWith('建立 1 筆，略過 1 筆：分類已封存');
    await flushList([FIXED]);
  });

  it('generate_button_is_disabled_while_pending', async () => {
    const { click, button, httpTesting, flushList, fixture } = await setup([]);
    await click('button.generate');
    expect(button('button.generate').disabled).toBe(true);
    expect(button('button.refresh').disabled).toBe(true);
    httpTesting.expectOne(GENERATE).flush({ created: [], skipped: [] });
    await fixture.whenStable();
    await flushList([]);
    expect(button('button.generate').disabled).toBe(false);
  });

  it('pay_opens_dialog_and_posts_with_version', async () => {
    const { menu, rootLoader, httpTesting, notifier, flushList, fixture } = await setup();
    await menu('p-fix', '付款');
    await (await rootLoader.getHarness(MatButtonHarness.with({ text: '付款' }))).click();
    await fixture.whenStable();
    // 對話框關閉動畫結束後才送出
    await vi.waitFor(() => {
      const request = httpTesting.expectOne({ method: 'POST', url: `${base}/p-fix/pay` });
      expect(request.request.body).toEqual({
        version: 5, date: toDateString(new Date()), accountId: 'acc-bank', amount: -100, loanAccountId: null, loanPrincipal: null,
      });
      request.flush({});
    });
    await fixture.whenStable();
    expect(notifier.show).toHaveBeenCalledWith('已付款');
    await flushList([{ ...FIXED, isPaid: true, paidTransactionId: 't1', version: 6 }, LOAN]);
  });

  it('locked_month_shows_locked_message', async () => {
    const { click, httpTesting, notifier, fixture } = await setup();
    await click('button.generate');
    httpTesting.expectOne(GENERATE).flush(
      { code: 'locked', detail: '2026-04-30 以前已鎖帳' }, { status: 422, statusText: 'Unprocessable Entity' });
    await fixture.whenStable();
    expect(notifier.show).toHaveBeenCalledWith('這個月份已鎖帳，不能異動');
  });

  it('conflict_on_delete_reloads', async () => {
    const { menu, rootLoader, httpTesting, notifier, flushList, fixture } = await setup();
    await menu('p-fix', '刪除');
    await (await rootLoader.getHarness(MatButtonHarness.with({ text: '刪除' }))).click();
    await fixture.whenStable();
    await vi.waitFor(() => httpTesting.expectOne({ method: 'DELETE', url: `${base}/p-fix?version=5` })
      .flush(null, { status: 409, statusText: 'Conflict' }));
    await fixture.whenStable();
    expect(notifier.show).toHaveBeenCalledWith(PLANNED_CONFLICT_MESSAGE);
    await flushList([FIXED, LOAN]);
  });

  it('recurring_tab_lists_items_with_description', async () => {
    const { el, openRecurring } = await setup();
    await openRecurring([MORTGAGE, ENDED]);
    const mortgage = el.querySelector('[data-recurring="r1"]')!;
    expect(mortgage.textContent).toContain('房屋貸款');
    expect(mortgage.textContent).toContain('每月，2026/01 起');
    expect(mortgage.classList).not.toContain('ended');
    const ended = el.querySelector('[data-recurring="r2"]')!;
    expect(ended.textContent).toContain('每年 3、9 月，2025/01–2025/12');
    expect(ended.textContent).toContain('已結束');
    expect(ended.classList).toContain('ended');
  });

  it('delete_in_use_shows_backend_message', async () => {
    const { openRecurring, menu, rootLoader, httpTesting, notifier, fixture } = await setup();
    await openRecurring([MORTGAGE]);
    await menu('r1', '刪除');
    await (await rootLoader.getHarness(MatButtonHarness.with({ text: '刪除' }))).click();
    await fixture.whenStable();
    const detail = '這個週期項目已產生過預定支出，不能刪除；請改設結束月份。';
    await vi.waitFor(() => httpTesting.expectOne({ method: 'DELETE', url: `${RECURRING}/r1?version=2` })
      .flush({ code: 'in-use', detail }, { status: 422, statusText: 'Unprocessable Entity' }));
    await fixture.whenStable();
    expect(notifier.show).toHaveBeenCalledWith(detail);
  });

  it('edit_puts_version_and_input', async () => {
    const { openRecurring, menu, rootLoader, httpTesting, notifier, fixture } = await setup();
    await openRecurring([MORTGAGE]);
    await menu('r1', '修改');
    await (await rootLoader.getHarness(MatButtonHarness.with({ text: '儲存' }))).click();
    await fixture.whenStable();
    await vi.waitFor(() => {
      const request = httpTesting.expectOne({ method: 'PUT', url: `${RECURRING}/r1` });
      expect(request.request.body).toEqual({
        version: 2,
        input: {
          categoryId: 'loan', accountId: 'acc-bank', defaultAmount: -3000, note: '房貸',
          frequency: 'Monthly', months: [], startMonth: 202601, endMonth: null,
        },
      });
      request.flush({ ...MORTGAGE, version: 3 });
    });
    await fixture.whenStable();
    expect(notifier.show).toHaveBeenCalledWith('已更新週期項目');
    await vi.waitFor(() => httpTesting.expectOne(RECURRING).flush([{ ...MORTGAGE, version: 3 }]));
  });
});
