import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MatButtonHarness } from '@angular/material/button/testing';
import { MatMenuHarness } from '@angular/material/menu/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { BookDto, PlannedExpenseDto } from '../../core/api/dto';
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
  return { fixture, httpTesting, loader, rootLoader, el, notifier, flushList, button, click, menu };
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
});
