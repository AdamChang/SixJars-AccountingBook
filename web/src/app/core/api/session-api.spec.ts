import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SessionApi } from './session-api';
import { MeDto } from './dto';

describe('SessionApi', () => {
  let api: SessionApi;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(SessionApi);
    httpTesting = TestBed.inject(HttpTestingController);
  });
  afterEach(() => httpTesting.verify());

  it('me_gets_api_me', () => {
    const me: MeDto = { subject: 's', email: null, books: [{ id: 'b1', name: '帳本' }] };
    let result: MeDto | undefined;
    api.me().subscribe((v) => (result = v));
    httpTesting.expectOne({ method: 'GET', url: '/api/me' }).flush(me);
    expect(result).toEqual(me);
  });

  it('fetchAntiforgeryToken_gets_token_endpoint', () => {
    api.fetchAntiforgeryToken().subscribe();
    httpTesting.expectOne({ method: 'GET', url: '/api/antiforgery/token' }).flush(null);
  });

  it('logout_posts_null_body', () => {
    api.logout().subscribe();
    const req = httpTesting.expectOne({ method: 'POST', url: '/auth/logout' });
    expect(req.request.body).toBeNull();
    req.flush(null, { status: 204, statusText: 'No Content' });
  });
});
