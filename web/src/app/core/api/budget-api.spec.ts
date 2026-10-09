import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { BudgetApi } from './budget-api';
import { BudgetSheetDto, CategoryBudgetDto } from './dto';

const SHEET: BudgetSheetDto = {
  rows: [{ categoryId: 'cat-food', defaultAmount: 5000, budget: 3000, source: 'Override', actual: 1000, remaining: 2000 }],
  totals: { budget: 3000, actual: 1000, remaining: 2000 },
};
const BUDGET: CategoryBudgetDto = { categoryId: 'cat-food', defaultAmount: 5000, overrides: [{ budgetMonth: 202602, amount: 3000 }] };

describe('BudgetApi', () => {
  let api: BudgetApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(BudgetApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('get_sends_budget_month', () => {
    let result: BudgetSheetDto | undefined;
    api.get('b1', 202602).subscribe(r => (result = r));
    const req = http.expectOne('/api/books/b1/budgets?budgetMonth=202602');
    expect(req.request.method).toBe('GET');
    req.flush(SHEET);
    expect(result).toEqual(SHEET);
  });

  it('set_default_puts_amount', () => {
    let result: CategoryBudgetDto | undefined;
    api.setDefault('b1', 'cat-food', 5000).subscribe(r => (result = r));
    const req = http.expectOne('/api/books/b1/budgets/cat-food/default');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ amount: 5000 });
    req.flush(BUDGET);
    expect(result).toEqual(BUDGET);
  });

  it('set_override_puts_amount_for_month', () => {
    api.setOverride('b1', 'cat-food', 202602, 3000).subscribe();
    const req = http.expectOne('/api/books/b1/budgets/cat-food/overrides/202602');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ amount: 3000 });
    req.flush(BUDGET);
  });

  it('remove_default_deletes', () => {
    let done = false;
    api.removeDefault('b1', 'cat-food').subscribe(() => (done = true));
    const req = http.expectOne('/api/books/b1/budgets/cat-food/default');
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
    expect(done).toBe(true);
  });

  it('remove_override_deletes_month', () => {
    api.removeOverride('b1', 'cat-food', 202602).subscribe();
    const req = http.expectOne('/api/books/b1/budgets/cat-food/overrides/202602');
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
  });
});
