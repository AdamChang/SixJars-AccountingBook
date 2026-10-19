import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { Router, provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { MeDto } from '../api/dto';
import { SessionService } from '../auth/session.service';
import { BrowserLocation } from '../browser-location';
import { apiErrorInterceptor } from '../errors/error-interceptor';
import { CREATE_FORBIDDEN_MESSAGE, UNAVAILABLE_MESSAGE } from '../errors/messages';
import { Notifier } from '../errors/notifier';
import { BookCreation } from './book-creation';
import { NewBookInput } from './create-book-dialog';

const INPUT: NewBookInput = { name: '家庭帳本', openingDate: '2026-10-19' };
const ME: MeDto = { subject: 's', email: 'a@b.c', books: [], canCreateBooks: true, remainingBookQuota: 5 };

interface SetupOptions { autoStart?: boolean; reloadRejects?: boolean }

async function setup(options: SetupOptions = {}) {
  const { autoStart = true, reloadRejects = false } = options;
  const dialog = { submitted: new Subject<NewBookInput>(), busy: signal(false), showError: vi.fn() };
  const close = vi.fn();
  const open = vi.fn(() => ({ componentInstance: dialog, close }));
  const notifier = { show: vi.fn() };
  const reload = vi.fn(async () => {
    if (reloadRejects) throw new Error('me failed');
    return ME;
  });
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(withInterceptors([apiErrorInterceptor])), provideHttpClientTesting(),
      provideRouter([]),
      { provide: MatDialog, useValue: { open } },
      { provide: Notifier, useValue: notifier },
      { provide: SessionService, useValue: { reload } },
      { provide: BrowserLocation, useValue: { assign: vi.fn(), currentPath: () => '/' } },
    ],
  });
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  const httpTesting = TestBed.inject(HttpTestingController);
  const service = TestBed.inject(BookCreation);
  if (autoStart) await service.start();
  // 使用者在對話框按「建立」，回傳送出的 POST
  const submit = () => {
    dialog.submitted.next(INPUT);
    return httpTesting.expectOne({ method: 'POST', url: '/api/books' });
  };
  return { service, open, dialog, close, notifier, reload, navigate, httpTesting, submit };
}

describe('BookCreation', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('success_reloads_session_then_opens_accounts_tab_of_new_book', async () => {
    const { submit, dialog, close, notifier, reload, navigate } = await setup();

    const request = submit();
    expect(request.request.body).toEqual(INPUT);
    expect(dialog.busy()).toBe(true);
    request.flush({ id: 'b9' }, { status: 201, statusText: 'Created' });

    await vi.waitFor(() => expect(notifier.show).toHaveBeenCalledWith('已建立帳本「家庭帳本」'));
    expect(close).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith(['/books', 'b9', 'settings'], { queryParams: { tab: 'accounts' } });
    // Q21：me 要先包含新帳本，工具列選單才看得到它
    expect(reload.mock.invocationCallOrder[0]).toBeLessThan(navigate.mock.invocationCallOrder[0]);
  });

  it('domain_error_keeps_dialog_open_with_backend_message', async () => {
    const { submit, dialog, close, navigate } = await setup();

    submit().flush({ code: 'duplicate-name', detail: '你已經有名稱為「家庭帳本」的帳本。' }, { status: 422, statusText: '' });

    expect(dialog.showError).toHaveBeenCalledWith({ kind: 'domain', code: 'duplicate-name', message: '你已經有名稱為「家庭帳本」的帳本。' });
    expect(dialog.busy()).toBe(false);
    expect(close).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
  });

  it('forbidden_closes_dialog_and_reloads_session', async () => {
    const { submit, close, notifier, reload } = await setup();

    submit().flush(null, { status: 403, statusText: '' });

    expect(close).toHaveBeenCalled();
    // interceptor 不會再額外顯示「暫時無法連線」（Task 1）
    expect(notifier.show).toHaveBeenCalledTimes(1);
    expect(notifier.show).toHaveBeenCalledWith(CREATE_FORBIDDEN_MESSAGE);
    expect(reload).toHaveBeenCalled();
  });

  it('unavailable_keeps_dialog_open_for_retry', async () => {
    const { submit, dialog, close, notifier } = await setup();

    submit().flush(null, { status: 503, statusText: '' });

    expect(notifier.show).toHaveBeenCalledWith(UNAVAILABLE_MESSAGE);
    expect(dialog.busy()).toBe(false);
    expect(dialog.showError).not.toHaveBeenCalled();
    expect(close).not.toHaveBeenCalled();
  });

  it('validation_error_keeps_dialog_open_with_field_errors', async () => {
    const { submit, dialog, close } = await setup();

    submit().flush({ errors: { openingDate: ['開帳日必填。'] } }, { status: 400, statusText: '' });

    expect(dialog.showError).toHaveBeenCalledWith({ kind: 'validation', fieldErrors: { openingDate: ['開帳日必填。'] } });
    expect(close).not.toHaveBeenCalled();
    expect(dialog.busy()).toBe(false);
  });

  it('second_start_while_dialog_is_loading_does_not_open_a_second_dialog', async () => {
    const { service, open } = await setup({ autoStart: false });

    await Promise.all([service.start(), service.start()]);

    expect(open).toHaveBeenCalledTimes(1);
  });

  it('dialog_chunk_load_failure_shows_unavailable_message_without_throwing', async () => {
    const { service, open, notifier } = await setup({ autoStart: false });
    vi.spyOn(service as any, 'loadDialog').mockRejectedValue(new Error('chunk'));

    await expect(service.start()).resolves.toBeUndefined();

    expect(notifier.show).toHaveBeenCalledTimes(1);
    expect(notifier.show).toHaveBeenCalledWith(UNAVAILABLE_MESSAGE);
    expect(open).not.toHaveBeenCalled();
  });

  it('success_still_opens_new_book_when_session_reload_fails', async () => {
    const { submit, notifier, reload, navigate } = await setup({ reloadRejects: true });

    submit().flush({ id: 'b9' }, { status: 201, statusText: 'Created' });

    await vi.waitFor(() => expect(notifier.show).toHaveBeenCalledWith('已建立帳本「家庭帳本」'));
    expect(reload).toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith(['/books', 'b9', 'settings'], { queryParams: { tab: 'accounts' } });
  });
});
