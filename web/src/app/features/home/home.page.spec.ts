import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { MeDto } from '../../core/api/dto';
import { BookCreation } from '../../core/book/book-creation';
import { SessionService } from '../../core/auth/session.service';
import { NO_BOOKS_CREATOR_MESSAGE, NO_BOOKS_MESSAGE } from '../../core/errors/messages';
import { HomePage } from './home.page';

function setup(books: MeDto['books'], canCreateBooks = false) {
  const me = signal<MeDto | undefined>({
    subject: 's', email: 'a@b.c', books, canCreateBooks, remainingBookQuota: canCreateBooks ? 5 : 0,
  });
  const start = vi.fn();
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: SessionService, useValue: { me } }, { provide: BookCreation, useValue: { start } }],
  });
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  const fixture = TestBed.createComponent(HomePage);
  fixture.detectChanges();
  return { fixture, navigate, start, me, el: fixture.nativeElement as HTMLElement };
}

describe('HomePage', () => {
  it('home_redirects_to_only_book', () => {
    const { navigate } = setup([{ id: 'b1', name: '我的帳本' }]);
    expect(navigate).toHaveBeenCalledWith(['/books', 'b1', 'transactions'], { replaceUrl: true });
  });

  it('home_lists_books_when_many', () => {
    const { navigate, el } = setup([{ id: 'b1', name: '甲' }, { id: 'b2', name: '乙' }]);
    expect(navigate).not.toHaveBeenCalled();
    const links = Array.from(el.querySelectorAll('a'));
    expect(links.map((a) => a.textContent?.trim())).toEqual(['甲', '乙']);
    expect(links.map((a) => a.getAttribute('href'))).toEqual(['/books/b1/transactions', '/books/b2/transactions']);
  });

  // 刪掉最後一本帳本後也會到這裡；Web 沒有建立帳本的入口，要告訴使用者下一步找誰（issue #6 後續）
  it('home_shows_next_step_hint_when_none', () => {
    const { navigate, el } = setup([]);
    expect(navigate).not.toHaveBeenCalled();
    expect(el.textContent).toContain(NO_BOOKS_MESSAGE);
    expect(NO_BOOKS_MESSAGE).toBe('沒有可存取的帳本。請管理者用 CLI 匯入帳本，或把你加為帳本成員。');
  });

  // 建立者沒有帳本也能登入（ADR 0011）：首頁直接給建立的入口（spec Q15）
  it('home_offers_book_creation_to_creators_without_books', async () => {
    const { el, start, me, fixture } = setup([], true);
    expect(el.textContent).toContain(NO_BOOKS_CREATOR_MESSAGE);
    expect(el.textContent).not.toContain(NO_BOOKS_MESSAGE);
    el.querySelector<HTMLButtonElement>('button.create-book')!.click();
    expect(start).toHaveBeenCalledTimes(1);

    // 建立時收到 403 會重新載入 me：被移出名單後回到請管理者協助的說明
    me.update(value => ({ ...value!, canCreateBooks: false, remainingBookQuota: 0 }));
    await fixture.whenStable();
    expect(el.querySelector('button.create-book')).toBeNull();
    expect(el.textContent).toContain(NO_BOOKS_MESSAGE);
  });
});
