import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { MeDto } from '../../core/api/dto';
import { SessionService } from '../../core/auth/session.service';
import { NO_BOOKS_MESSAGE } from '../../core/errors/messages';
import { HomePage } from './home.page';

function setup(books: MeDto['books']) {
  const me = signal<MeDto | undefined>({ subject: 's', email: 'a@b.c', books });
  TestBed.configureTestingModule({
    providers: [provideRouter([]), { provide: SessionService, useValue: { me } }],
  });
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  const fixture = TestBed.createComponent(HomePage);
  fixture.detectChanges();
  return { fixture, navigate, el: fixture.nativeElement as HTMLElement };
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

  it('home_shows_message_when_none', () => {
    const { navigate, el } = setup([]);
    expect(navigate).not.toHaveBeenCalled();
    expect(el.textContent).toContain(NO_BOOKS_MESSAGE);
  });
});
