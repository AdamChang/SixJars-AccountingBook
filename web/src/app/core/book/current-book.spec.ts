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

  it('ignores_stale_response_after_book_switch', async () => {
    const first = current.load('b1');
    const second = current.load('b2');
    httpTesting.expectOne('/api/books/b2').flush(book('b2'));
    await second;
    httpTesting.expectOne('/api/books/b1').flush(book('b1'));
    await first;

    expect(current.book()?.id).toBe('b2');
  });

  it('clears_cache_on_failure_then_caches_success', async () => {
    const failed = current.load('b1');
    httpTesting.expectOne('/api/books/b1').flush(null, { status: 500, statusText: 'err' });
    await expect(failed).rejects.toBeDefined();

    const retry = current.load('b1');
    httpTesting.expectOne('/api/books/b1').flush(book('b1'));
    await retry;

    // 第三次走快取，不再發請求（afterEach 的 verify 會檢查）
    expect((await current.load('b1')).id).toBe('b1');
  });
});
