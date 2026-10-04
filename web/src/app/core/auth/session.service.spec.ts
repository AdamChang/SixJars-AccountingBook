import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BrowserLocation } from '../browser-location';
import { MeDto } from '../api/dto';
import { SessionService } from './session.service';

describe('SessionService', () => {
  let service: SessionService;
  let httpTesting: HttpTestingController;
  const assign = vi.fn();
  const me: MeDto = { subject: 's1', email: 'a@b.c', books: [] };

  beforeEach(() => {
    assign.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: BrowserLocation, useValue: { assign, currentPath: () => '/' } },
      ],
    });
    service = TestBed.inject(SessionService);
    httpTesting = TestBed.inject(HttpTestingController);
  });
  afterEach(() => httpTesting.verify());

  it('loads_me_then_fetches_antiforgery_token_once', async () => {
    const first = service.load();
    const second = service.load();

    const meRequest = httpTesting.expectOne('/api/me');
    // me 回應之前不可以先要 token
    httpTesting.expectNone('/api/antiforgery/token');
    meRequest.flush(me);
    await vi.waitFor(() => httpTesting.expectOne('/api/antiforgery/token').flush(null));

    expect(await first).toEqual(me);
    expect(await second).toEqual(me);
    expect(service.me()).toEqual(me);

    // 之後的呼叫走快取，不再發請求（afterEach 的 verify 會檢查）
    expect(await service.load()).toEqual(me);
  });

  it('retries_after_me_request_fails', async () => {
    const failed = service.load();
    httpTesting.expectOne('/api/me').flush(null, { status: 500, statusText: 'err' });
    await expect(failed).rejects.toBeDefined();

    const retry = service.load();
    httpTesting.expectOne('/api/me').flush(me);
    await vi.waitFor(() => httpTesting.expectOne('/api/antiforgery/token').flush(null));
    expect(await retry).toEqual(me);
  });

  it('logout_posts_then_reloads_root', async () => {
    const done = service.logout();
    httpTesting.expectOne({ method: 'POST', url: '/auth/logout' }).flush(null);
    await done;
    expect(assign).toHaveBeenCalledWith('/');
  });
});
