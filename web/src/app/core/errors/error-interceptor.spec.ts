import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { BrowserLocation } from '../browser-location';
import { ApiError } from './api-error';
import { apiErrorInterceptor } from './error-interceptor';
import { UNAVAILABLE_MESSAGE } from './messages';
import { Notifier } from './notifier';

describe('apiErrorInterceptor', () => {
  const assign = vi.fn();
  const show = vi.fn();
  let http: HttpClient;
  let controller: HttpTestingController;

  beforeEach(() => {
    assign.mockReset();
    show.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([apiErrorInterceptor])),
        provideHttpClientTesting(),
        { provide: BrowserLocation, useValue: { assign, currentPath: () => '/books/b1/transactions?month=202601' } },
        { provide: Notifier, useValue: { show } },
      ],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  it('redirects_to_login_on_401', () => {
    const error = vi.fn();
    http.get('/api/x').subscribe({ error });
    controller.expectOne('/api/x').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(assign).toHaveBeenCalledWith('/auth/login?returnUrl=%2Fbooks%2Fb1%2Ftransactions%3Fmonth%3D202601');
    expect(error).not.toHaveBeenCalled();
  });

  it('notifies_and_rethrows_on_network_error', () => {
    let received: ApiError | undefined;
    http.get('/api/x').subscribe({ error: (e) => (received = e) });
    controller.expectOne('/api/x').error(new ProgressEvent('error'));

    expect(show).toHaveBeenCalledWith(UNAVAILABLE_MESSAGE);
    expect(received).toEqual({ kind: 'unavailable' });
  });

  it('rethrows_validation_without_notifying', () => {
    let received: ApiError | undefined;
    http.post('/api/x', {}).subscribe({ error: (e) => (received = e) });
    controller
      .expectOne('/api/x')
      .flush({ errors: { amount: ['錯'] } }, { status: 400, statusText: 'Bad Request' });

    expect(received).toEqual({ kind: 'validation', fieldErrors: { amount: ['錯'] } });
    expect(show).not.toHaveBeenCalled();
  });
});
