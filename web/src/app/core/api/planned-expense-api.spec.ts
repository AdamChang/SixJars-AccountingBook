import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PlannedExpenseApi } from './planned-expense-api';

describe('PlannedExpenseApi', () => {
  let api: PlannedExpenseApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(PlannedExpenseApi);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('lists_by_budget_month', () => {
    api.list('b1', 202604).subscribe();
    http.expectOne({ method: 'GET', url: '/api/books/b1/planned-expenses?budgetMonth=202604' }).flush([]);
  });

  it('generate_and_refresh_post_month_in_query_without_body', () => {
    api.generate('b1', 202604).subscribe();
    api.refresh('b1', 202604).subscribe();
    const generate = http.expectOne({ method: 'POST', url: '/api/books/b1/planned-expenses/generate?budgetMonth=202604' });
    const refresh = http.expectOne({ method: 'POST', url: '/api/books/b1/planned-expenses/refresh?budgetMonth=202604' });
    expect(generate.request.body).toBeNull();
    expect(refresh.request.body).toBeNull();
    generate.flush({ created: [], skipped: [] });
    refresh.flush({ updated: [], skipped: [] });
  });

  it('update_sends_version_and_input', () => {
    const input = { budgetMonth: 202604, categoryId: 'c', accountId: null, estimatedAmount: -100, note: null };
    api.update('b1', 'p1', 7, input).subscribe();
    const request = http.expectOne({ method: 'PUT', url: '/api/books/b1/planned-expenses/p1' });
    expect(request.request.body).toEqual({ version: 7, input });
    request.flush({});
  });

  it('delete_sends_version_in_query', () => {
    api.delete('b1', 'p1', 7).subscribe();
    http.expectOne({ method: 'DELETE', url: '/api/books/b1/planned-expenses/p1?version=7' }).flush(null);
  });

  it('pay_posts_body', () => {
    const body = { version: 3, date: '2026-04-05', accountId: 'a', amount: -100, loanAccountId: null, loanPrincipal: null };
    api.pay('b1', 'p1', body).subscribe();
    const request = http.expectOne({ method: 'POST', url: '/api/books/b1/planned-expenses/p1/pay' });
    expect(request.request.body).toEqual(body);
    request.flush({});
  });
});
