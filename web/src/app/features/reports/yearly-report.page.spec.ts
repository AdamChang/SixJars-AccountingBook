import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ChartConfiguration } from 'chart.js';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { YearlyFiguresDto, YearlyReportDto } from '../../core/api/dto';
import { BrowserLocation } from '../../core/browser-location';
import { apiErrorInterceptor } from '../../core/errors/error-interceptor';
import { Notifier } from '../../core/errors/notifier';
import { CHART_FACTORY, ChartFactory } from '../../shared/chart';
import { YearlyReportPage } from './yearly-report.page';

const figures = (january: number, share: number | null, average: number | null): YearlyFiguresDto =>
  ({ months: Array.from({ length: 12 }, (_, i) => (i === 0 ? january : 0)), total: january, shareOfIncome: share, monthlyAverage: average });
const REPORT: YearlyReportDto = {
  year: 2026,
  averageDivisor: 4,
  income: { kind: 'Income', figures: figures(40000, 1, 10000), lines: [] },
  expenses: (['Floating', 'Fixed', 'Loan', 'Special'] as const).map(kind => ({
    kind, figures: figures(kind === 'Floating' ? 1000 : 0, kind === 'Floating' ? 0.025 : 0, 250), lines: [],
  })),
  expenseTotal: figures(1000, 0.025, 250),
  planning: { allocation: [], withdrawal: [], return: [] },
};

async function setup(report: YearlyReportDto = REPORT) {
  const charts: ChartConfiguration[] = [];
  const factory: ChartFactory = (_canvas, config) => {
    charts.push(config);
    return { destroy: vi.fn() };
  };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([
        { path: 'books/:bookId/reports/yearly', component: YearlyReportPage },
        { path: 'books/:bookId/reports/monthly', children: [] },
      ]),
      provideHttpClient(withInterceptors([apiErrorInterceptor])),
      provideHttpClientTesting(),
      { provide: CHART_FACTORY, useValue: factory },
      { provide: Notifier, useValue: { show: vi.fn() } },
      { provide: BrowserLocation, useValue: { assign: vi.fn(), currentPath: () => '/' } },
    ],
  });
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl('/books/b1/reports/yearly?month=202604', YearlyReportPage);
  const fixture = harness.fixture;
  const http = TestBed.inject(HttpTestingController);
  await fixture.whenStable();
  http.expectOne('/api/books/b1/reports/yearly?year=2026').flush(report);
  await fixture.whenStable();
  return { fixture, http, charts, el: fixture.nativeElement as HTMLElement };
}

describe('YearlyReportPage', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('requests_the_year_of_the_month_param_and_shows_twelve_months', async () => {
    const { el } = await setup();
    const headers = Array.from(el.querySelectorAll('thead th')).map(th => th.textContent!.trim());
    expect(headers).toEqual(['項目', '1月', '2月', '3月', '4月', '5月', '6月', '7月', '8月', '9月', '10月', '11月', '12月', '總計', '佔收入比', '月平均']);
    expect(el.querySelector('.year')!.textContent).toContain('2026');
    expect(el.textContent).toContain('月平均以 1–4 月計算');
  });

  it('formats_share_and_average', async () => {
    const { el } = await setup();
    const floating = el.querySelector<HTMLElement>('[data-section="Floating"]')!;
    expect(floating.querySelector('.share')!.textContent!.trim()).toBe('2.5%');
    expect(floating.querySelector('.average')!.textContent!.trim()).toBe('250');
  });

  it('shows_dash_for_future_year_average', async () => {
    const future = { ...REPORT, averageDivisor: null, expenseTotal: figures(1000, 0.025, null) };
    const { el } = await setup(future);
    expect(el.querySelector('[data-row="expense-total"] .average')!.textContent!.trim()).toBe('—');
    expect(el.textContent).toContain('未來的年份不計算月平均');
  });

  it('year_nav_moves_the_month_param_by_a_year', async () => {
    const { el, fixture, http } = await setup();
    el.querySelector<HTMLButtonElement>('button[aria-label="下一年"]')!.click();
    await fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/books/b1/reports/yearly?month=202704');
    http.expectOne('/api/books/b1/reports/yearly?year=2027').flush(REPORT);
  });

  it('draws_a_stacked_bar_of_the_four_natures', async () => {
    const { charts } = await setup();
    expect(charts).toHaveLength(1);
    expect(charts[0].type).toBe('bar');
    expect(charts[0].data.datasets).toHaveLength(4);
  });
});
