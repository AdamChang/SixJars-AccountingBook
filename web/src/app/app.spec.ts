import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { BookDto, MeDto } from './core/api/dto';
import { SessionService } from './core/auth/session.service';
import { CurrentBook } from './core/book/current-book';
import { LoadingIndicator } from './core/loading/loading-indicator';
import { App } from './app';

function setup(me?: MeDto, book?: Partial<BookDto>) {
  const logout = vi.fn();
  TestBed.configureTestingModule({
    imports: [App],
    providers: [
      provideRouter([]),
      { provide: SessionService, useValue: { me: signal(me), logout } },
      { provide: CurrentBook, useValue: { book: signal(book) } },
      { provide: LoadingIndicator, useValue: { visible: signal(false) } },
    ],
  });
  const fixture = TestBed.createComponent(App);
  fixture.detectChanges();
  return { fixture, logout, el: fixture.nativeElement as HTMLElement };
}

describe('App', () => {
  it('shell_hides_toolbar_before_sign_in', () => {
    const { el } = setup();
    expect(el.querySelector('mat-toolbar')).toBeNull();
  });

  it('shell_shows_book_name_email_and_logout', () => {
    const { el, logout, fixture } = setup(
      { subject: 's', email: 'a@b.c', books: [] },
      { id: 'b1', name: '我的帳本' },
    );
    const toolbar = el.querySelector('mat-toolbar')!;
    expect(toolbar.textContent).toContain('我的帳本');
    expect(toolbar.textContent).toContain('a@b.c');
    const hrefs = Array.from(toolbar.querySelectorAll('a')).map((a) => a.getAttribute('href'));
    expect(hrefs).toEqual(['/books/b1/transactions', '/books/b1/summary', '/books/b1/settings']);
    const button = Array.from(toolbar.querySelectorAll('button')).find((b) => b.textContent?.includes('登出'))!;
    button.click();
    fixture.detectChanges();
    expect(logout).toHaveBeenCalled();
  });
});
