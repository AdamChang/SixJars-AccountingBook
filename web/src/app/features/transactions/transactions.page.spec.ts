import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MAT_DATE_LOCALE, provideNativeDateAdapter } from '@angular/material/core';
import { MatButtonHarness } from '@angular/material/button/testing';
import { MatDialog } from '@angular/material/dialog';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { TransactionDto } from '../../core/api/dto';
import { CurrentBook } from '../../core/book/current-book';
import { BrowserLocation } from '../../core/browser-location';
import { apiErrorInterceptor } from '../../core/errors/error-interceptor';
import { CONFLICT_MESSAGE } from '../../core/errors/messages';
import { Notifier } from '../../core/errors/notifier';
import { ConfirmDialog } from '../../shared/confirm-dialog';
import { BOOK, transactionDtoFrom } from './testing/book-fixture';
import { TransactionForm } from './transaction-form';
import { TransactionsPage } from './transactions.page';

const TX: TransactionDto = transactionDtoFrom(
  {
    kind: 'Expense', date: '2026-03-15', budgetMonth: null, amount: -100, accountId: 'acc-cash',
    counterAccountId: null, categoryId: null, planningFundId: null, loanPrincipal: null, note: null,
  },
  { id: 'tx-1', version: 3 },
);
const listUrl = (month: number) => `/api/books/${BOOK.id}/transactions?budgetMonth=${month}`;
const DELETE_URL = `/api/books/${BOOK.id}/transactions/${TX.id}?version=${TX.version}`;

async function setup(url: string, confirmed = true) {
  const notifier = { show: vi.fn() };
  const dialog = { open: vi.fn(() => ({ afterClosed: () => of(confirmed) })) };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([{ path: 'books/:bookId', children: [{ path: 'transactions', component: TransactionsPage }] }]),
      provideHttpClient(withInterceptors([apiErrorInterceptor])),
      provideHttpClientTesting(),
      provideNativeDateAdapter(),
      { provide: MAT_DATE_LOCALE, useValue: 'zh-TW' },
      { provide: CurrentBook, useValue: { book: signal(BOOK) } },
      { provide: Notifier, useValue: notifier },
      { provide: BrowserLocation, useValue: { assign: vi.fn(), currentPath: () => '/' } },
      { provide: MatDialog, useValue: dialog },
    ],
  });
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(url, TransactionsPage);
  const httpTesting = TestBed.inject(HttpTestingController);
  const fixture = harness.fixture;
  const loader = TestbedHarnessEnvironment.loader(fixture);
  const el = fixture.nativeElement as HTMLElement;
  return {
    notifier, dialog, httpTesting, el,
    async flushList(month: number, transactions: TransactionDto[]) {
      httpTesting.expectOne(listUrl(month)).flush(transactions);
      await fixture.whenStable();
    },
    form: () => fixture.debugElement.query(By.directive(TransactionForm)).componentInstance as TransactionForm,
    editingRows: () => el.querySelectorAll('tr.mat-mdc-row.editing').length,
    async clickRow() {
      el.querySelector<HTMLElement>('tr.mat-mdc-row')!.click();
      await fixture.whenStable();
    },
    async clickDelete() {
      await (await loader.getHarness(MatButtonHarness.with({ selector: '.delete' }))).click();
      await fixture.whenStable();
    },
    button: (label: string) => loader.getHarness(MatButtonHarness.with({ selector: `[aria-label="${label}"]` })),
    stable: () => fixture.whenStable(),
  };
}

describe('TransactionsPage', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    vi.useRealTimers();
  });

  it('loads_transactions_for_query_month', async () => {
    const page = await setup('/books/book-1/transactions?month=202603');
    await page.flushList(202603, [TX]);
    expect(page.el.querySelectorAll('tr.mat-mdc-row')).toHaveLength(1);
    expect(page.el.textContent).toContain('2026/03');
  });

  it('falls_back_to_current_month_for_invalid_query', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 4, 10));
    const page = await setup('/books/book-1/transactions?month=202613');
    await page.flushList(202605, []);
    expect(page.el.textContent).toContain('2026/05');
  });

  it('navigates_and_reloads_when_month_changes', async () => {
    const page = await setup('/books/book-1/transactions?month=202603');
    await page.flushList(202603, []);
    await (await page.button('上個月')).click();
    await page.stable();
    expect(TestBed.inject(Router).url).toBe('/books/book-1/transactions?month=202602');
    await page.flushList(202602, []);
  });

  it('reloads_after_save', async () => {
    const page = await setup('/books/book-1/transactions?month=202603');
    await page.flushList(202603, [TX]);
    await page.clickRow();
    expect(page.editingRows()).toBe(1);
    page.form().saved.emit(TX);
    await page.stable();
    await page.flushList(202603, [TX]);
    expect(page.editingRows()).toBe(0);
  });

  it('deletes_with_version_after_confirm', async () => {
    const page = await setup('/books/book-1/transactions?month=202603');
    await page.flushList(202603, [TX]);
    await page.clickDelete();
    expect(page.dialog.open).toHaveBeenCalledWith(ConfirmDialog, { data: '確定要刪除 2026-03-15 支出 -100？' });
    page.httpTesting.expectOne({ method: 'DELETE', url: DELETE_URL }).flush(null);
    await page.stable();
    expect(page.notifier.show).toHaveBeenCalledWith('已刪除');
    await page.flushList(202603, []);
  });

  it('does_not_delete_when_cancelled', async () => {
    const page = await setup('/books/book-1/transactions?month=202603', false);
    await page.flushList(202603, [TX]);
    await page.clickDelete();
    expect(page.dialog.open).toHaveBeenCalledOnce();
    expect(page.notifier.show).not.toHaveBeenCalled();
  });

  it('reloads_on_delete_conflict', async () => {
    const page = await setup('/books/book-1/transactions?month=202603');
    await page.flushList(202603, [TX]);
    await page.clickDelete();
    page.httpTesting.expectOne({ method: 'DELETE', url: DELETE_URL }).flush(null, { status: 409, statusText: 'Conflict' });
    await page.stable();
    expect(page.notifier.show).toHaveBeenCalledWith(CONFLICT_MESSAGE);
    await page.flushList(202603, [TX]);
  });

  it('stops_editing_when_edited_row_is_deleted', async () => {
    const page = await setup('/books/book-1/transactions?month=202603');
    await page.flushList(202603, [TX]);
    await page.clickRow();
    expect(page.form().editing()).toBe(TX);
    await page.clickDelete();
    page.httpTesting.expectOne({ method: 'DELETE', url: DELETE_URL }).flush(null);
    await page.stable();
    expect(page.form().editing()).toBeNull();
    await page.flushList(202603, []);
  });
});
