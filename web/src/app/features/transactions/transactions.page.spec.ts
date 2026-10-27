import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MAT_DATE_LOCALE, provideNativeDateAdapter } from '@angular/material/core';
import { MatButtonHarness } from '@angular/material/button/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { MatDialog } from '@angular/material/dialog';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of } from 'rxjs';
import { BookDto, TransactionDto } from '../../core/api/dto';
import { CurrentBook } from '../../core/book/current-book';
import { BrowserLocation } from '../../core/browser-location';
import { apiErrorInterceptor } from '../../core/errors/error-interceptor';
import { CONFLICT_MESSAGE } from '../../core/errors/messages';
import { Notifier } from '../../core/errors/notifier';
import { ConfirmDialog } from '../../shared/confirm-dialog';
import { EditTransactionDialog } from './edit-transaction-dialog';
import { BOOK, transactionDtoFrom } from './testing/book-fixture';
import { TransactionForm } from './transaction-form';
import { TransactionsPage } from './transactions.page';

const TX: TransactionDto = transactionDtoFrom(
  {
    kind: 'Expense', date: '2026-03-15', budgetMonth: null, amount: -100, accountId: 'acc-cash',
    counterAccountId: null, categoryId: null, planningFundId: null, loanPrincipal: null, note: null, tagIds: [],
  },
  { id: 'tx-1', version: 3 },
);
// 加上已封存的主分類與帳戶，確認選單會列出它們（Q7）
const ARCHIVED_AT = '2026-02-01T00:00:00+00:00';
const FILTER_BOOK: BookDto = {
  ...BOOK,
  accounts: [
    ...BOOK.accounts,
    { id: 'acc-old', name: '舊帳戶', type: 'Cash', openingBalance: 0, countsAsAvailableCash: true, sortOrder: 5, archivedAt: ARCHIVED_AT },
  ],
  categories: [
    ...BOOK.categories,
    { id: 'cat-old', name: '舊分類', kind: 'Expense', nature: 'Floating', parentId: null, sortOrder: 1, archivedAt: ARCHIVED_AT },
  ],
};
const listUrl = (month: number) => `/api/books/${BOOK.id}/transactions?budgetMonth=${month}`;
const DELETE_URL = `/api/books/${BOOK.id}/transactions/${TX.id}?version=${TX.version}`;

// 對話框關閉時的結果：確認對話框為 boolean，編輯對話框為 'saved' | 'stale' | undefined（取消）
// 用函式而非預設參數，否則傳 undefined 會變成預設值
const CANCELLED = () => undefined;

async function setup(url: string, closedWith: () => unknown = () => true, book: BookDto = BOOK) {
  const notifier = { show: vi.fn() };
  const dialog = { open: vi.fn(() => ({ afterClosed: () => of(closedWith()) })) };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([{ path: 'books/:bookId', children: [{ path: 'transactions', component: TransactionsPage }] }]),
      provideHttpClient(withInterceptors([apiErrorInterceptor])),
      provideHttpClientTesting(),
      provideNativeDateAdapter(),
      { provide: MAT_DATE_LOCALE, useValue: 'zh-TW' },
      { provide: CurrentBook, useValue: { book: signal(book) } },
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
    async clickRow() {
      el.querySelector<HTMLElement>('tr.mat-mdc-row')!.click();
      await fixture.whenStable();
    },
    async clickDelete() {
      await (await loader.getHarness(MatButtonHarness.with({ selector: '.delete' }))).click();
      await fixture.whenStable();
    },
    button: (label: string) => loader.getHarness(MatButtonHarness.with({ selector: `[aria-label="${label}"]` })),
    tagFilter: () => loader.getHarness(MatSelectHarness.with({ selector: '.tag-filter' })),
    categoryFilter: () => loader.getHarness(MatSelectHarness.with({ selector: '.category-filter' })),
    accountFilter: () => loader.getHarness(MatSelectHarness.with({ selector: '.account-filter' })),
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
    expect(page.el.querySelector('.no-accounts')).toBeNull();
  });

  // 新建立的帳本沒有帳戶，封存的也不能用：提示到設定頁的帳戶分頁新增（spec F6）
  it('hints_settings_accounts_tab_when_no_active_account', async () => {
    const archived = BOOK.accounts.map(account => ({ ...account, archivedAt: '2026-01-01T00:00:00Z' }));
    const page = await setup('/books/book-1/transactions?month=202603', undefined, { ...BOOK, accounts: archived });
    await page.flushList(202603, []);

    const link = page.el.querySelector<HTMLAnchorElement>('.no-accounts a')!;
    expect(link.getAttribute('href')).toBe('/books/book-1/settings?tab=accounts');
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

  describe('tag filter', () => {
    const tagListUrl = (month: number, tagId: string) => `${listUrl(month)}&tagId=${tagId}`;

    it('loads_with_tag_query_and_shows_it_selected', async () => {
      const page = await setup('/books/book-1/transactions?month=202603&tag=tag-trip');
      page.httpTesting.expectOne(tagListUrl(202603, 'tag-trip')).flush([]);
      await page.stable();
      expect(await (await page.tagFilter()).getValueText()).toBe('旅遊');
      expect(page.el.textContent).toContain('這個月沒有符合篩選條件的交易');
    });

    it('options_include_archived_tags_in_sort_order', async () => {
      const page = await setup('/books/book-1/transactions?month=202603');
      await page.flushList(202603, []);
      const select = await page.tagFilter();
      // 沒有篩選時也要看得出是「全部標籤」（mat-select 把 null 當成未選取，手動驗證時發現是空白）
      expect(await select.getValueText()).toBe('全部標籤');
      await select.open();
      const texts = await Promise.all((await select.getOptions()).map(option => option.getText()));
      expect(texts).toEqual(['全部標籤', '工作', '旅遊', '舊專案（已封存）']);
      await select.close();
    });

    it('selecting_tag_sets_query_and_month_change_keeps_it', async () => {
      const page = await setup('/books/book-1/transactions?month=202603');
      await page.flushList(202603, []);
      const select = await page.tagFilter();
      await select.open();
      await select.clickOptions({ text: '工作' });
      await page.stable();
      expect(TestBed.inject(Router).url).toBe('/books/book-1/transactions?month=202603&tag=tag-work');
      page.httpTesting.expectOne(tagListUrl(202603, 'tag-work')).flush([]);
      await page.stable();

      await (await page.button('上個月')).click();
      await page.stable();
      expect(TestBed.inject(Router).url).toBe('/books/book-1/transactions?month=202602&tag=tag-work');
      page.httpTesting.expectOne(tagListUrl(202602, 'tag-work')).flush([]);
      await page.stable();
    });

    it('choosing_all_tags_removes_query', async () => {
      const page = await setup('/books/book-1/transactions?month=202603&tag=tag-trip');
      page.httpTesting.expectOne(tagListUrl(202603, 'tag-trip')).flush([]);
      await page.stable();
      const select = await page.tagFilter();
      await select.open();
      await select.clickOptions({ text: '全部標籤' });
      await page.stable();
      expect(TestBed.inject(Router).url).toBe('/books/book-1/transactions?month=202603');
      await page.flushList(202603, []);
    });

    it('unknown_tag_query_is_ignored', async () => {
      const page = await setup('/books/book-1/transactions?month=202603&tag=tag-ghost');
      // 不帶 tagId（Q11）
      await page.flushList(202603, []);
      expect(await (await page.tagFilter()).getValueText()).toBe('全部標籤');
      expect(page.el.textContent).toContain('這個月還沒有交易');
    });
  });

  describe('category filter', () => {
    const categoryListUrl = (month: number, categoryId: string) => `${listUrl(month)}&categoryId=${categoryId}`;

    it('loads_with_category_query_and_shows_it_selected', async () => {
      const page = await setup('/books/book-1/transactions?month=202603&category=cat-food');
      page.httpTesting.expectOne(categoryListUrl(202603, 'cat-food')).flush([]);
      await page.stable();
      expect(await (await page.categoryFilter()).getValueText()).toBe('飲食');
      expect(page.el.textContent).toContain('這個月沒有符合篩選條件的交易');
    });

    it('options_are_main_categories_grouped_by_kind_including_archived', async () => {
      const page = await setup('/books/book-1/transactions?month=202603', undefined, FILTER_BOOK);
      await page.flushList(202603, []);
      const select = await page.categoryFilter();
      expect(await select.getValueText()).toBe('全部分類');
      await select.open();
      const groups = await select.getOptionGroups();
      expect(await Promise.all(groups.map(group => group.getLabelText()))).toEqual(['收入', '支出']);
      const texts = await Promise.all((await select.getOptions()).map(option => option.getText()));
      expect(texts).toEqual(['全部分類', '薪資', '飲食', '舊分類（已封存）']);
      await select.close();
    });

    it('selecting_category_sets_query_and_month_change_keeps_it', async () => {
      const page = await setup('/books/book-1/transactions?month=202603');
      await page.flushList(202603, []);
      const select = await page.categoryFilter();
      await select.open();
      await select.clickOptions({ text: '飲食' });
      await page.stable();
      expect(TestBed.inject(Router).url).toBe('/books/book-1/transactions?month=202603&category=cat-food');
      page.httpTesting.expectOne(categoryListUrl(202603, 'cat-food')).flush([]);
      await page.stable();

      await (await page.button('上個月')).click();
      await page.stable();
      expect(TestBed.inject(Router).url).toBe('/books/book-1/transactions?month=202602&category=cat-food');
      page.httpTesting.expectOne(categoryListUrl(202602, 'cat-food')).flush([]);
      await page.stable();
    });

    // 子分類（F3）與不存在的 id（Q11）都視為沒有篩選
    it.each(['cat-lunch', 'cat-ghost'])('ignores_category_query_%s', async categoryId => {
      const page = await setup(`/books/book-1/transactions?month=202603&category=${categoryId}`);
      await page.flushList(202603, []);
      expect(await (await page.categoryFilter()).getValueText()).toBe('全部分類');
    });
  });

  describe('account filter', () => {
    it('loads_with_account_query_and_shows_it_selected', async () => {
      const page = await setup('/books/book-1/transactions?month=202603&account=acc-bank');
      page.httpTesting.expectOne(`${listUrl(202603)}&accountId=acc-bank`).flush([]);
      await page.stable();
      expect(await (await page.accountFilter()).getValueText()).toBe('銀行');
      expect(page.el.textContent).toContain('這個月沒有符合篩選條件的交易');
    });

    it('options_include_archived_accounts_in_sort_order', async () => {
      const page = await setup('/books/book-1/transactions?month=202603', undefined, FILTER_BOOK);
      await page.flushList(202603, []);
      const select = await page.accountFilter();
      expect(await select.getValueText()).toBe('全部帳戶');
      await select.open();
      const texts = await Promise.all((await select.getOptions()).map(option => option.getText()));
      expect(texts).toEqual(['全部帳戶', '現金', '銀行', '信用卡', '悠遊卡', '房貸', '舊帳戶（已封存）']);
      await select.close();
    });

    it('selecting_account_combines_with_other_filters', async () => {
      const page = await setup('/books/book-1/transactions?month=202603&tag=tag-trip&category=cat-food');
      page.httpTesting.expectOne(`${listUrl(202603)}&tagId=tag-trip&categoryId=cat-food`).flush([]);
      await page.stable();
      const select = await page.accountFilter();
      await select.open();
      await select.clickOptions({ text: '銀行' });
      await page.stable();
      expect(TestBed.inject(Router).url).toBe('/books/book-1/transactions?month=202603&tag=tag-trip&category=cat-food&account=acc-bank');
      page.httpTesting.expectOne(`${listUrl(202603)}&tagId=tag-trip&categoryId=cat-food&accountId=acc-bank`).flush([]);
      await page.stable();
    });

    it('unknown_account_query_is_ignored', async () => {
      const page = await setup('/books/book-1/transactions?month=202603&account=acc-ghost');
      await page.flushList(202603, []);
      expect(await (await page.accountFilter()).getValueText()).toBe('全部帳戶');
    });
  });

  it('reloads_after_create', async () => {
    const page = await setup('/books/book-1/transactions?month=202603');
    await page.flushList(202603, []);
    page.form().saved.emit(TX);
    await page.stable();
    await page.flushList(202603, [TX]);
  });

  it('create_form_never_enters_edit_mode', async () => {
    const page = await setup('/books/book-1/transactions?month=202603', CANCELLED);
    await page.flushList(202603, [TX]);
    await page.clickRow();
    expect(page.form().editing()).toBeNull();
  });

  describe('edit dialog', () => {
    it('opens_with_clicked_transaction', async () => {
      const page = await setup('/books/book-1/transactions?month=202603', CANCELLED);
      await page.flushList(202603, [TX]);
      await page.clickRow();
      expect(page.dialog.open).toHaveBeenCalledWith(EditTransactionDialog, expect.objectContaining({
        data: { book: BOOK, transaction: TX }, disableClose: true,
      }));
    });

    it.each(['saved', 'stale'])('reloads_when_closed_as_%s', async result => {
      const page = await setup('/books/book-1/transactions?month=202603', () => result);
      await page.flushList(202603, [TX]);
      await page.clickRow();
      await page.flushList(202603, [TX]);
    });

    it('does_not_reload_when_cancelled', async () => {
      const page = await setup('/books/book-1/transactions?month=202603', CANCELLED);
      await page.flushList(202603, [TX]);
      await page.clickRow();
      // afterEach 的 verify 確認沒有多出的清單請求
      expect(page.dialog.open).toHaveBeenCalledOnce();
    });
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
    const page = await setup('/books/book-1/transactions?month=202603', () => false);
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
});
