import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ChartConfiguration } from 'chart.js';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { MonthlyReportDto, ReportLineDto } from '../../core/api/dto';
import { BrowserLocation } from '../../core/browser-location';
import { apiErrorInterceptor } from '../../core/errors/error-interceptor';
import { Notifier } from '../../core/errors/notifier';
import { CHART_FACTORY, ChartFactory } from '../../shared/chart';
import { MonthlyReportPage } from './monthly-report.page';

const line = (kind: ReportLineDto['kind'], id: string, name: string, amount: number, extra: Partial<ReportLineDto> = {}): ReportLineDto =>
  ({ kind, id, name, amount, principal: null, interest: null, children: [], ...extra });
const REPORT: MonthlyReportDto = {
  budgetMonth: 202604,
  monthlyDisposable: 12345,
  income: { kind: 'Income', total: 50000, unpaid: null, lines: [line('Category', 'cat-salary', '薪資', 50000)] },
  expenses: [
    { kind: 'Floating', total: 150, unpaid: null, lines: [line('Category', 'cat-food', '飲食', 150, {
      children: [line('Category', 'cat-breakfast', '早餐', 100), line('Unassigned', 'cat-food', '（未分子類）', 50)],
    })] },
    { kind: 'Fixed', total: 0, unpaid: 2000, lines: [] },
    { kind: 'Loan', total: 20000, unpaid: 0, lines: [line('LoanAccount', 'acc-loan', '房貸', 20000, { principal: 15000, interest: 5000 })] },
    { kind: 'Special', total: 0, unpaid: 0, lines: [] },
  ],
  expenseTotal: 20150,
  unpaidTotal: 2000,
  planning: [{ fundId: 'fund-1', name: '財務自由', allocation: 3000, withdrawal: 0, return: 0 }],
};
const URL = '/api/books/b1/reports/monthly?budgetMonth=202604';

async function setup(report: MonthlyReportDto = REPORT) {
  const charts: ChartConfiguration[] = [];
  const factory: ChartFactory = (_canvas, config) => {
    charts.push(config);
    return { destroy: vi.fn() };
  };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([{ path: 'books/:bookId/reports/monthly', component: MonthlyReportPage }]),
      provideHttpClient(withInterceptors([apiErrorInterceptor])),
      provideHttpClientTesting(),
      { provide: CHART_FACTORY, useValue: factory },
      { provide: Notifier, useValue: { show: vi.fn() } },
      { provide: BrowserLocation, useValue: { assign: vi.fn(), currentPath: () => '/' } },
    ],
  });
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl('/books/b1/reports/monthly?month=202604', MonthlyReportPage);
  const fixture = harness.fixture;
  const http = TestBed.inject(HttpTestingController);
  await fixture.whenStable();
  http.expectOne(URL).flush(report);
  await fixture.whenStable();
  const el = fixture.nativeElement as HTMLElement;
  const rowNames = () => Array.from(el.querySelectorAll<HTMLElement>('[data-line] .line-name')).map(n => n.textContent!.trim());
  return { fixture, http, el, charts, rowNames };
}

describe('MonthlyReportPage', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('shows_disposable_totals_and_the_difference_note', async () => {
    const { el } = await setup();
    expect(el.querySelector('.disposable')!.textContent).toContain('12,345');
    expect(el.querySelector('.expense-total')!.textContent).toContain('20,150');
    expect(el.querySelector('.unpaid-total')!.textContent).toContain('2,000');
    expect(el.querySelector('.difference-note')!.textContent).toContain('不等於月可用餘額');
  });

  it('expands_main_category_to_show_subs_and_unassigned', async () => {
    const { el, fixture, rowNames } = await setup();
    expect(rowNames()).not.toContain('早餐');
    const toggleButton = el.querySelector<HTMLButtonElement>('button[aria-label="展開 飲食"]')!;
    expect(toggleButton.getAttribute('aria-expanded')).toBe('false');
    toggleButton.click();
    await fixture.whenStable();
    expect(rowNames()).toEqual(expect.arrayContaining(['早餐', '（未分子類）']));
    expect(el.querySelector('button[aria-label="收合 飲食"]')!.getAttribute('aria-expanded')).toBe('true');
  });

  it('lists_unpaid_only_for_sections_with_unpaid_amounts', async () => {
    const { el } = await setup();
    const unpaid = Array.from(el.querySelectorAll<HTMLElement>('[data-unpaid]')).map(r => [r.dataset['unpaid'], r.textContent!.replace(/\s+/g, ' ').trim()]);
    expect(unpaid).toEqual([['Fixed', expect.stringContaining('2,000')]]);
  });

  it('shows_principal_and_interest_for_loan_accounts', async () => {
    const { el } = await setup();
    expect(el.querySelector('[data-line="LoanAccount:acc-loan"]')!.textContent).toContain('本金 15,000・利息 5,000');
  });

  it('draws_nature_and_floating_doughnuts', async () => {
    const { charts } = await setup();
    expect(charts.map(c => c.type)).toEqual(['doughnut', 'doughnut']);
  });

  it('shows_an_empty_message_instead_of_charts_without_spending', async () => {
    const { charts, el } = await setup({ ...REPORT, expenses: REPORT.expenses.map(s => ({ ...s, total: 0, lines: [] })), expenseTotal: 0 });
    expect(charts).toHaveLength(0);
    expect(el.textContent).toContain('本月沒有支出');
  });

  it('month_nav_updates_the_month_query_param', async () => {
    const { el, fixture, http } = await setup();
    el.querySelector<HTMLButtonElement>('button[aria-label="下個月"]')!.click();
    await fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/books/b1/reports/monthly?month=202605');
    http.expectOne('/api/books/b1/reports/monthly?budgetMonth=202605').flush(REPORT);
  });

  // 換月後新的報表還沒回來或載入失敗：不能在 5 月的標題下繼續顯示 4 月的數字（最終審查 Important #1）
  it('hides_previous_month_figures_while_loading_and_after_a_failed_load', async () => {
    const { el, fixture, http } = await setup();
    el.querySelector<HTMLButtonElement>('button[aria-label="下個月"]')!.click();
    await fixture.whenStable();
    expect(el.querySelector('.disposable')).toBeNull();
    expect(el.textContent).toContain('載入中');
    http.expectOne('/api/books/b1/reports/monthly?budgetMonth=202605').flush(null, { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();
    expect(el.querySelector('.disposable')).toBeNull();
    expect(el.querySelector('table')).toBeNull();
    expect(el.textContent).toContain('無法載入這個月份的報表');
  });
});
