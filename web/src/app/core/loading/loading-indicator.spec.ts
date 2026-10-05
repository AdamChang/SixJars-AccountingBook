import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { LoadingIndicator, loadingInterceptor } from './loading-indicator';

describe('LoadingIndicator', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let indicator: LoadingIndicator;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([loadingInterceptor])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
    indicator = TestBed.inject(LoadingIndicator);
  });

  afterEach(() => {
    controller.verify();
    vi.useRealTimers();
  });

  it('shows_only_after_300ms', () => {
    http.get('/a').subscribe();
    vi.advanceTimersByTime(299);
    expect(indicator.visible()).toBe(false);
    vi.advanceTimersByTime(1);
    expect(indicator.visible()).toBe(true);
    controller.expectOne('/a').flush({});
    expect(indicator.visible()).toBe(false);
  });

  it('waits_for_all_overlapping_requests', () => {
    http.get('/a').subscribe();
    http.get('/b').subscribe();
    vi.advanceTimersByTime(300);
    expect(indicator.visible()).toBe(true);
    controller.expectOne('/a').flush({});
    expect(indicator.visible()).toBe(true);
    controller.expectOne('/b').flush({});
    expect(indicator.visible()).toBe(false);
  });

  it('never_shows_for_fast_requests', () => {
    http.get('/a').subscribe();
    vi.advanceTimersByTime(200);
    controller.expectOne('/a').flush({});
    vi.advanceTimersByTime(1000);
    expect(indicator.visible()).toBe(false);
  });
});
