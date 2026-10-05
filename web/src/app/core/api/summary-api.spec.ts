import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SummaryApi } from './summary-api';
import { LedgerSummaryDto } from './dto';

describe('SummaryApi', () => {
  let api: SummaryApi;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(SummaryApi);
    httpTesting = TestBed.inject(HttpTestingController);
  });
  afterEach(() => httpTesting.verify());

  it('get_gets_summary_with_budget_month_only', () => {
    const summary: LedgerSummaryDto = {
      budgetMonth: 202601, asOf: '2026-01-31', monthlyDisposable: 1, yearToDate: 2,
      availableCash: 3, availableCashWithEWallets: 4, accounts: [], planningFunds: [],
    };
    let result: LedgerSummaryDto | undefined;
    api.get('b1', 202601).subscribe((v) => (result = v));
    httpTesting
      .expectOne({ method: 'GET', url: '/api/books/b1/summary?budgetMonth=202601' })
      .flush(summary);
    expect(result).toEqual(summary);
  });
});
