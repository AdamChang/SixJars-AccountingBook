import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRouteSnapshot, RouterStateSnapshot, Router, UrlTree, convertToParamMap } from '@angular/router';
import { BrowserLocation } from '../browser-location';
import { Notifier } from '../errors/notifier';
import { apiErrorInterceptor } from '../errors/error-interceptor';
import { bookGuard } from './book.guard';

describe('bookGuard', () => {
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([apiErrorInterceptor])),
        provideHttpClientTesting(),
        { provide: BrowserLocation, useValue: { assign: vi.fn(), currentPath: () => '/' } },
        { provide: Notifier, useValue: { show: vi.fn() } },
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
  });
  afterEach(() => httpTesting.verify());

  const run = () => {
    const route = { paramMap: convertToParamMap({ bookId: 'b1' }) } as ActivatedRouteSnapshot;
    return TestBed.runInInjectionContext(() => bookGuard(route, {} as RouterStateSnapshot)) as Promise<boolean | UrlTree>;
  };

  it('book_guard_redirects_home_when_book_not_found', async () => {
    const result = run();
    httpTesting.expectOne('/api/books/b1').flush(null, { status: 404, statusText: 'Not Found' });

    const tree = await result;
    expect(tree).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(tree as UrlTree)).toBe('/');
  });

  it('book_guard_rethrows_other_errors', async () => {
    const result = run();
    httpTesting.expectOne('/api/books/b1').flush(null, { status: 500, statusText: 'err' });
    await expect(result).rejects.toEqual({ kind: 'unavailable' });
  });

  it('book_guard_allows_when_book_loads', async () => {
    const result = run();
    httpTesting.expectOne('/api/books/b1').flush({
      id: 'b1', name: 'x', openingDate: '2026-01-01', lockDate: null,
      accounts: [], planningFunds: [], categories: [],
    });
    expect(await result).toBe(true);
  });
});
