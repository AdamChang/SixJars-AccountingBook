import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { BookApi } from './book-api';
import { BookDto } from './dto';

describe('BookApi', () => {
  let api: BookApi;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(BookApi);
    httpTesting = TestBed.inject(HttpTestingController);
  });
  afterEach(() => httpTesting.verify());

  it('get_gets_book_by_id', () => {
    const book: BookDto = {
      id: 'b1', name: '帳本', openingDate: '2026-01-01', lockDate: null,
      accounts: [], planningFunds: [], categories: [],
    };
    let result: BookDto | undefined;
    api.get('b1').subscribe((v) => (result = v));
    httpTesting.expectOne({ method: 'GET', url: '/api/books/b1' }).flush(book);
    expect(result).toEqual(book);
  });

  it('update_account_puts_name_and_cash_flag', async () => {
    const done = firstValueFrom(api.updateAccount('b1', 'a1', { name: '郵局', countsAsAvailableCash: false }));
    const request = httpTesting.expectOne({ method: 'PUT', url: '/api/books/b1/accounts/a1' });
    expect(request.request.body).toEqual({ name: '郵局', countsAsAvailableCash: false });
    request.flush(null, { status: 204, statusText: 'No Content' });
    await done;
  });

  it('update_planning_fund_and_category_put_to_their_paths', async () => {
    const fund = firstValueFrom(api.updatePlanningFund('b1', 'f1', { name: '旅行基金' }));
    const fundRequest = httpTesting.expectOne({ method: 'PUT', url: '/api/books/b1/planning-funds/f1' });
    expect(fundRequest.request.body).toEqual({ name: '旅行基金' });
    fundRequest.flush(null, { status: 204, statusText: '' });
    await fund;
    const category = firstValueFrom(api.updateCategory('b1', 'c1', { name: '飲食', nature: 'Special' }));
    const categoryRequest = httpTesting.expectOne({ method: 'PUT', url: '/api/books/b1/categories/c1' });
    expect(categoryRequest.request.body).toEqual({ name: '飲食', nature: 'Special' });
    categoryRequest.flush(null, { status: 204, statusText: '' });
    await category;
  });

  it('archive_unarchive_and_remove_use_kind_path', async () => {
    const archive = firstValueFrom(api.archive('b1', 'categories', 'c1'));
    httpTesting.expectOne({ method: 'POST', url: '/api/books/b1/categories/c1/archive' }).flush(null, { status: 204, statusText: '' });
    await archive;
    const unarchive = firstValueFrom(api.unarchive('b1', 'planning-funds', 'f1'));
    httpTesting.expectOne({ method: 'POST', url: '/api/books/b1/planning-funds/f1/unarchive' }).flush(null, { status: 204, statusText: '' });
    await unarchive;
    const remove = firstValueFrom(api.remove('b1', 'accounts', 'a1'));
    httpTesting.expectOne({ method: 'DELETE', url: '/api/books/b1/accounts/a1' }).flush(null, { status: 204, statusText: '' });
    await remove;
  });

  it('reorder_sends_whole_group', async () => {
    const done = firstValueFrom(api.reorder('b1', 'categories', { kind: 'Expense', parentId: null, ids: ['c2', 'c1'] }));
    const request = httpTesting.expectOne({ method: 'PUT', url: '/api/books/b1/categories/order' });
    expect(request.request.body).toEqual({ kind: 'Expense', parentId: null, ids: ['c2', 'c1'] });
    request.flush(null, { status: 204, statusText: '' });
    await done;
  });

  it('add_and_lock_date', async () => {
    const add = firstValueFrom(api.add('b1', 'planning-funds', { name: '旅遊基金', openingBalance: 0 }));
    httpTesting.expectOne({ method: 'POST', url: '/api/books/b1/planning-funds' }).flush({ id: 'f9' }, { status: 201, statusText: '' });
    expect(await add).toEqual({ id: 'f9' });
    const lock = firstValueFrom(api.setLockDate('b1', null));
    const request = httpTesting.expectOne({ method: 'PUT', url: '/api/books/b1/lock-date' });
    expect(request.request.body).toEqual({ lockDate: null });
    request.flush(null, { status: 204, statusText: '' });
    await lock;
  });
});
