import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MatMenuHarness } from '@angular/material/menu/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { BookDto, BudgetRowDto, BudgetSheetDto, CategoryBudgetDto } from '../../core/api/dto';
import { CurrentBook } from '../../core/book/current-book';
import { BrowserLocation } from '../../core/browser-location';
import { apiErrorInterceptor } from '../../core/errors/error-interceptor';
import { Notifier } from '../../core/errors/notifier';
import { BOOK } from '../transactions/testing/book-fixture';
import { BudgetsPage } from './budgets.page';

// BOOK 只有一個浮動主分類（飲食）；補上第二個
const book: BookDto = {
  ...BOOK,
  categories: [
    ...BOOK.categories,
    { id: 'cat-fun', name: '娛樂', kind: 'Expense', nature: 'Floating', parentId: null, sortOrder: 1, archivedAt: null },
  ],
};

const FOOD: BudgetRowDto = { categoryId: 'cat-food', defaultAmount: 5000, budget: 3000, source: 'Override', actual: 1000, remaining: 2000 };
const FUN: BudgetRowDto = { categoryId: 'cat-fun', defaultAmount: null, budget: null, source: null, actual: 400, remaining: null };
const sheet = (...rows: BudgetRowDto[]): BudgetSheetDto => ({
  rows,
  totals: { budget: 0, actual: 0, remaining: 0 },
});

const base = `/api/books/${book.id}/budgets`;
const LIST = `${base}?budgetMonth=202604`;
const OVERRIDE = `${base}/cat-food/overrides/202604`;

async function setup(initial: BudgetSheetDto = sheet(FOOD, FUN)) {
  const notifier = { show: vi.fn() };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([{ path: 'books/:bookId/budgets', component: BudgetsPage }]),
      provideHttpClient(withInterceptors([apiErrorInterceptor])),
      provideHttpClientTesting(),
      { provide: CurrentBook, useValue: { book: signal(book) } },
      { provide: Notifier, useValue: notifier },
      { provide: BrowserLocation, useValue: { assign: vi.fn(), currentPath: () => '/' } },
    ],
  });
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(`/books/${book.id}/budgets?month=202604`, BudgetsPage);
  const fixture = harness.fixture;
  const httpTesting = TestBed.inject(HttpTestingController);
  const loader = TestbedHarnessEnvironment.loader(fixture);
  const el = fixture.nativeElement as HTMLElement;
  const flushList = async (value: BudgetSheetDto) => {
    await fixture.whenStable();
    httpTesting.expectOne(LIST).flush(value);
    await fixture.whenStable();
  };
  await flushList(initial);
  const row = (id: string) => el.querySelector<HTMLElement>(`[data-row="${id}"]`)!;
  const menu = async (id: string, item: string) =>
    (await loader.getHarness(MatMenuHarness.with({ selector: `[data-menu="${id}"]` }))).clickItem({ text: item });
  // 選單關閉動畫結束後才出現文字框
  const editor = async (id: string) => {
    await vi.waitFor(() => expect(row(id).querySelector('input.amount-input')).not.toBeNull());
    return row(id).querySelector<HTMLInputElement>('input.amount-input')!;
  };
  const type = async (input: HTMLInputElement, text: string) => {
    input.value = text;
    input.dispatchEvent(new Event('input'));
    await fixture.whenStable();
  };
  const saveButton = (id: string) => row(id).querySelector<HTMLButtonElement>('button.save')!;
  return { harness, fixture, httpTesting, el, notifier, flushList, row, menu, editor, type, saveButton };
}

describe('BudgetsPage', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('loads_rows_for_query_month', async () => {
    const { el, row } = await setup();
    expect([...el.querySelectorAll('[data-row] .name')].map(n => n.textContent!.trim())).toEqual(['飲食', '娛樂']);
    expect(row('cat-food').querySelector('.source')!.textContent!.trim()).toBe('本月');
    expect(row('cat-fun').querySelector('.source')!.textContent!.trim()).toBe('未設');
    expect(row('cat-fun').querySelector('.remaining')!.textContent!.trim()).toBe('');
    expect(row('cat-fun').querySelector('.usage')!.textContent!.trim()).toBe('');
    expect(row('cat-fun').querySelector('mat-progress-bar')).toBeNull();
    expect(row('cat-food').querySelector('mat-progress-bar')).not.toBeNull();
  });

  it('over_budget_row_is_marked', async () => {
    const { row } = await setup(sheet({ ...FOOD, actual: 3500, remaining: -500 }, FUN));
    expect(row('cat-food').classList).toContain('over-budget');
    expect(row('cat-food').querySelector('.remaining')!.classList).toContain('red-ink');
    expect(row('cat-fun').classList).not.toContain('over-budget');
  });

  it('edit_override_puts_amount_and_updates_row_without_reload', async () => {
    const { menu, editor, type, saveButton, httpTesting, row, fixture } = await setup();
    await menu('cat-food', '設定本月預算');
    await type(await editor('cat-food'), '1,200');
    saveButton('cat-food').click();
    await fixture.whenStable();
    const request = httpTesting.expectOne({ method: 'PUT', url: OVERRIDE });
    expect(request.request.body).toEqual({ amount: 1200 });
    const budget: CategoryBudgetDto = { categoryId: 'cat-food', defaultAmount: 5000, overrides: [{ budgetMonth: 202604, amount: 1200 }] };
    request.flush(budget);
    await fixture.whenStable();
    expect(row('cat-food').querySelector('input.amount-input')).toBeNull();
    expect(row('cat-food').querySelector('.budget-amount')!.textContent!.trim()).toBe('1,200');
    expect(row('cat-food').querySelector('.source')!.textContent!.trim()).toBe('本月');
    expect(row('cat-food').querySelector('.remaining')!.textContent!.trim()).toBe('200');
    httpTesting.expectNone(LIST);
  });

  it('edit_default_prefills_default_amount', async () => {
    const { menu, editor } = await setup();
    await menu('cat-food', '設定預設預算');
    expect((await editor('cat-food')).value).toBe('5000');
  });

  // 手動驗證發現：選單關閉後焦點回到 ⋮，要再點一次文字框，Enter／Esc 也沒作用
  it('choosing_edit_from_menu_focuses_the_amount_box', async () => {
    const { menu, editor, fixture } = await setup();
    await menu('cat-food', '設定本月預算');
    const input = await editor('cat-food');
    await vi.waitFor(() => expect(document.activeElement).toBe(input));
    await new Promise(resolve => setTimeout(resolve, 200));
    await fixture.whenStable();
    expect(document.activeElement).toBe(input);
  });

  it('invalid_amount_disables_save', async () => {
    const { menu, editor, type, saveButton } = await setup();
    await menu('cat-fun', '設定本月預算');
    const input = await editor('cat-fun');
    expect(input.value).toBe('');
    await type(input, '-5');
    expect(saveButton('cat-fun').disabled).toBe(true);
    await type(input, '300');
    expect(saveButton('cat-fun').disabled).toBe(false);
  });

  it('clear_override_deletes_and_reloads', async () => {
    const { menu, httpTesting, flushList, fixture } = await setup();
    await menu('cat-food', '清除本月預算');
    await vi.waitFor(() => httpTesting.expectOne({ method: 'DELETE', url: OVERRIDE })
      .flush(null, { status: 204, statusText: 'No Content' }));
    await fixture.whenStable();
    await flushList(sheet({ ...FOOD, budget: 5000, source: 'Default', remaining: 4000 }, FUN));
  });

  it('not_found_shows_message_and_reloads', async () => {
    const { menu, httpTesting, notifier, flushList, fixture } = await setup();
    await menu('cat-food', '清除本月預算');
    await vi.waitFor(() => httpTesting.expectOne({ method: 'DELETE', url: OVERRIDE })
      .flush(null, { status: 404, statusText: 'Not Found' }));
    await fixture.whenStable();
    expect(notifier.show).toHaveBeenCalledWith('這筆預算已不存在');
    await flushList(sheet(FOOD, FUN));
  });

  // 預算不做樂觀並行（Q3），但兩個裝置同時清掉最後一個值時，第二個 DELETE 影響 0 列，後端回 409
  it('conflict_shows_message_and_reloads', async () => {
    const { menu, httpTesting, notifier, flushList, fixture } = await setup();
    await menu('cat-food', '清除本月預算');
    await vi.waitFor(() => httpTesting.expectOne({ method: 'DELETE', url: OVERRIDE })
      .flush(null, { status: 409, statusText: 'Conflict' }));
    await fixture.whenStable();
    expect(notifier.show).toHaveBeenCalledWith('這筆預算已在其他裝置修改');
    await flushList(sheet(FOOD, FUN));
  });

  it('domain_error_shows_backend_message', async () => {
    const { menu, editor, type, saveButton, httpTesting, notifier, fixture } = await setup();
    await menu('cat-food', '設定本月預算');
    await type(await editor('cat-food'), '100');
    saveButton('cat-food').click();
    await fixture.whenStable();
    const detail = '預算只限浮動支出的主分類，「x」不是。';
    httpTesting.expectOne({ method: 'PUT', url: OVERRIDE })
      .flush({ code: 'rule', detail }, { status: 422, statusText: 'Unprocessable Entity' });
    await fixture.whenStable();
    expect(notifier.show).toHaveBeenCalledWith(detail);
  });

  // 最終審查 #1：PUT 進行中換月，回應不能套到新月份的列上（M1 的覆寫值不是 M2 的「本月」）
  it('save_response_after_month_change_reloads_instead_of_applying', async () => {
    const { harness, menu, editor, type, saveButton, httpTesting, row, fixture } = await setup();
    await menu('cat-food', '設定本月預算');
    await type(await editor('cat-food'), '1200');
    saveButton('cat-food').click();
    await fixture.whenStable();
    const request = httpTesting.expectOne({ method: 'PUT', url: OVERRIDE });
    await harness.navigateByUrl(`/books/${book.id}/budgets?month=202605`);
    await fixture.whenStable();
    const may = { ...FOOD, budget: 5000, source: 'Default' as const, remaining: 4000 };
    httpTesting.expectOne(`${base}?budgetMonth=202605`).flush(sheet(may, FUN));
    await fixture.whenStable();
    request.flush({ categoryId: 'cat-food', defaultAmount: 5000, overrides: [{ budgetMonth: 202604, amount: 1200 }] });
    await fixture.whenStable();
    expect(row('cat-food').querySelector('.source')!.textContent!.trim()).toBe('預設');
    httpTesting.expectOne(`${base}?budgetMonth=202605`).flush(sheet(may, FUN));
    await fixture.whenStable();
    expect(row('cat-food').querySelector('.budget-amount')!.textContent!.trim()).toBe('5,000');
  });

  // 最終審查 #1：換月時立刻取消編輯，不等新的表回來（否則 Enter 會把上個月的預填值寫進新月份）
  it('month_change_cancels_editing_immediately', async () => {
    const { harness, menu, editor, httpTesting, row, fixture } = await setup();
    await menu('cat-food', '設定本月預算');
    await editor('cat-food');
    await harness.navigateByUrl(`/books/${book.id}/budgets?month=202605`);
    await fixture.whenStable();
    expect(row('cat-food').querySelector('input.amount-input')).toBeNull();
    httpTesting.expectOne(`${base}?budgetMonth=202605`).flush(sheet(FOOD, FUN));
  });
});
