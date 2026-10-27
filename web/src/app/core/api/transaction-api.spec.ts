import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TransactionApi } from './transaction-api';
import { TransactionDto, TransactionInput } from './dto';

const input: TransactionInput = {
  kind: 'Expense', date: '2026-01-15', budgetMonth: null, amount: -100, accountId: 'a1',
  counterAccountId: null, categoryId: 'c1', planningFundId: null, loanPrincipal: null, note: null, tagIds: [],
};

const saved: TransactionDto = {
  id: 't1', kind: 'Expense', date: '2026-01-15', budgetMonth: 202601, amount: -100,
  accountId: 'a1', counterAccountId: null, categoryId: 'c1', planningFundId: null,
  loanPrincipal: null, loanInterest: null, note: null,
  postings: [{ accountId: 'a1', amount: -100 }], version: 1, tagIds: [],
};

describe('TransactionApi', () => {
  let api: TransactionApi;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(TransactionApi);
    httpTesting = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    httpTesting.verify();
    // 清掉 cookie，避免影響其他測試
    document.cookie = 'XSRF-TOKEN=; expires=Thu, 01 Jan 1970 00:00:00 GMT';
  });

  it('list_gets_transactions_for_budget_month', () => {
    let result: TransactionDto[] | undefined;
    api.list('b1', 202601).subscribe((v) => (result = v));
    httpTesting
      .expectOne({ method: 'GET', url: '/api/books/b1/transactions?budgetMonth=202601' })
      .flush([saved]);
    expect(result).toEqual([saved]);
  });

  it('list_adds_tag_id_only_when_given', () => {
    api.list('b1', 202610, { tagId: 't1' }).subscribe();
    httpTesting.expectOne({ method: 'GET', url: '/api/books/b1/transactions?budgetMonth=202610&tagId=t1' }).flush([]);
    api.list('b1', 202610, { tagId: null }).subscribe();
    httpTesting.expectOne({ method: 'GET', url: '/api/books/b1/transactions?budgetMonth=202610' }).flush([]);
  });

  it('list_adds_only_given_filters_in_fixed_order', () => {
    api.list('b1', 202610, { accountId: 'a1', categoryId: 'c1', tagId: 't1' }).subscribe();
    httpTesting
      .expectOne({ method: 'GET', url: '/api/books/b1/transactions?budgetMonth=202610&tagId=t1&categoryId=c1&accountId=a1' })
      .flush([]);
    api.list('b1', 202610, { categoryId: 'c1', accountId: undefined }).subscribe();
    httpTesting.expectOne({ method: 'GET', url: '/api/books/b1/transactions?budgetMonth=202610&categoryId=c1' }).flush([]);
  });

  it('create_posts_input', () => {
    let result: TransactionDto | undefined;
    api.create('b1', input).subscribe((v) => (result = v));
    const req = httpTesting.expectOne({ method: 'POST', url: '/api/books/b1/transactions' });
    expect(req.request.body).toEqual(input);
    req.flush(saved);
    expect(result).toEqual(saved);
  });

  it('update_puts_version_and_input', () => {
    api.update('b1', 't1', 3, input).subscribe();
    const req = httpTesting.expectOne({ method: 'PUT', url: '/api/books/b1/transactions/t1' });
    expect(req.request.body).toEqual({ version: 3, input });
    req.flush(saved);
  });

  it('delete_sends_version_in_query', () => {
    api.delete('b1', 't1', 3).subscribe();
    httpTesting
      .expectOne({ method: 'DELETE', url: '/api/books/b1/transactions/t1?version=3' })
      .flush(null, { status: 204, statusText: 'No Content' });
  });

  it('sends_xsrf_header_on_unsafe_requests', () => {
    document.cookie = 'XSRF-TOKEN=abc';
    api.create('b1', input).subscribe();
    expect(
      httpTesting.expectOne('/api/books/b1/transactions').request.headers.get('X-XSRF-TOKEN'),
    ).toBe('abc');
  });
});
