import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BookDto } from '../api/dto';
import { CurrentBook } from './current-book';

const book = (id: string): BookDto => ({
  id, name: `帳本${id}`, openingDate: '2026-01-01', lockDate: null,
  accounts: [], planningFunds: [], categories: [],
});

describe('CurrentBook', () => {
  let current: CurrentBook;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    current = TestBed.inject(CurrentBook);
    httpTesting = TestBed.inject(HttpTestingController);
  });
  afterEach(() => httpTesting.verify());

  it('current_book_reuses_cached_book_for_same_id', async () => {
    const first = current.load('b1');
    httpTesting.expectOne('/api/books/b1').flush(book('b1'));
    expect((await first).id).toBe('b1');
    expect(current.book()?.id).toBe('b1');

    expect((await current.load('b1')).id).toBe('b1');
  });

  it('current_book_refetches_when_book_changes', async () => {
    const first = current.load('b1');
    httpTesting.expectOne('/api/books/b1').flush(book('b1'));
    await first;

    const second = current.load('b2');
    httpTesting.expectOne('/api/books/b2').flush(book('b2'));
    expect((await second).id).toBe('b2');
    expect(current.book()?.id).toBe('b2');
  });
});
