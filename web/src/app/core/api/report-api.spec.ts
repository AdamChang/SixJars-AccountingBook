import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MonthlyReportDto, YearlyReportDto } from './dto';
import { ReportApi } from './report-api';

const SECTION = { total: 0, unpaid: null, lines: [] };
const FIGURES = { months: Array(12).fill(0), total: 0, shareOfIncome: null, monthlyAverage: null };
const MONTHLY: MonthlyReportDto = {
  budgetMonth: 202602, monthlyDisposable: 100, income: { kind: 'Income', ...SECTION },
  expenses: [], expenseTotal: 0, unpaidTotal: 0, planning: [],
};
const YEARLY: YearlyReportDto = {
  year: 2026, averageDivisor: 12, income: { kind: 'Income', figures: FIGURES, lines: [] },
  expenses: [], expenseTotal: FIGURES, planning: { allocation: [], withdrawal: [], return: [] },
};

describe('ReportApi', () => {
  let api: ReportApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(ReportApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('monthly_sends_budget_month', () => {
    let result: MonthlyReportDto | undefined;
    api.monthly('b1', 202602).subscribe(r => (result = r));
    const req = http.expectOne('/api/books/b1/reports/monthly?budgetMonth=202602');
    expect(req.request.method).toBe('GET');
    req.flush(MONTHLY);
    expect(result).toEqual(MONTHLY);
  });

  it('yearly_sends_year', () => {
    let result: YearlyReportDto | undefined;
    api.yearly('b1', 2026).subscribe(r => (result = r));
    const req = http.expectOne('/api/books/b1/reports/yearly?year=2026');
    expect(req.request.method).toBe('GET');
    req.flush(YEARLY);
    expect(result).toEqual(YEARLY);
  });
});
