import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
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
});
