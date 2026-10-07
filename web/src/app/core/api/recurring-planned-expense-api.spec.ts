import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { RecurringPlannedExpenseInput } from './dto';
import { RecurringPlannedExpenseApi } from './recurring-planned-expense-api';

const input: RecurringPlannedExpenseInput = {
  categoryId: 'c', accountId: null, defaultAmount: -100, note: null,
  frequency: 'Yearly', months: [1, 7], startMonth: 202601, endMonth: null,
};

describe('RecurringPlannedExpenseApi', () => {
  let api: RecurringPlannedExpenseApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(RecurringPlannedExpenseApi);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('lists_all_items_of_book', () => {
    api.list('b1').subscribe();
    http.expectOne({ method: 'GET', url: '/api/books/b1/recurring-planned-expenses' }).flush([]);
  });

  it('create_posts_input_as_body', () => {
    api.create('b1', input).subscribe();
    const request = http.expectOne({ method: 'POST', url: '/api/books/b1/recurring-planned-expenses' });
    expect(request.request.body).toEqual(input);
    request.flush({});
  });

  it('update_sends_version_and_input', () => {
    api.update('b1', 'r1', 4, input).subscribe();
    const request = http.expectOne({ method: 'PUT', url: '/api/books/b1/recurring-planned-expenses/r1' });
    expect(request.request.body).toEqual({ version: 4, input });
    request.flush({});
  });

  it('delete_sends_version_in_query', () => {
    api.delete('b1', 'r1', 4).subscribe();
    http.expectOne({ method: 'DELETE', url: '/api/books/b1/recurring-planned-expenses/r1?version=4' }).flush(null);
  });
});
