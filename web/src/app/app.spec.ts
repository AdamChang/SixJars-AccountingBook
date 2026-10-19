import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MatMenuHarness } from '@angular/material/menu/testing';
import { provideRouter } from '@angular/router';
import { BookDto, MeDto } from './core/api/dto';
import { SessionService } from './core/auth/session.service';
import { BookCreation } from './core/book/book-creation';
import { CurrentBook } from './core/book/current-book';
import { LoadingIndicator } from './core/loading/loading-indicator';
import { App } from './app';

const BOOK = { id: 'b1', name: '我的帳本' };
const me = (overrides: Partial<MeDto> = {}): MeDto => ({
  subject: 's', email: 'a@b.c', canCreateBooks: false, remainingBookQuota: 0,
  books: [BOOK, { id: 'b2', name: '另一本' }], ...overrides,
});

function setup(user?: MeDto, book?: Partial<BookDto>) {
  const logout = vi.fn();
  const start = vi.fn();
  TestBed.configureTestingModule({
    imports: [App],
    providers: [
      provideRouter([]),
      { provide: SessionService, useValue: { me: signal(user), logout } },
      { provide: CurrentBook, useValue: { book: signal(book) } },
      { provide: LoadingIndicator, useValue: { visible: signal(false) } },
      // 真的 BookCreation 需要 HttpClient 與 MatDialog；流程本身在 book-creation.spec 測
      { provide: BookCreation, useValue: { start } },
    ],
  });
  const fixture = TestBed.createComponent(App);
  fixture.detectChanges();
  const loader = TestbedHarnessEnvironment.loader(fixture);
  const openBookMenu = async () => {
    const menu = await loader.getHarness(MatMenuHarness.with({ selector: '.book-name' }));
    await menu.open();
    return menu;
  };
  return { fixture, logout, start, openBookMenu, el: fixture.nativeElement as HTMLElement };
}

describe('App', () => {
  it('shell_hides_toolbar_before_sign_in', () => {
    const { el } = setup();
    expect(el.querySelector('mat-toolbar')).toBeNull();
  });

  it('shell_shows_book_name_email_and_logout', () => {
    const { el, logout, fixture } = setup(me({ books: [] }), BOOK);
    const toolbar = el.querySelector('mat-toolbar')!;
    expect(toolbar.textContent).toContain('我的帳本');
    expect(toolbar.textContent).toContain('a@b.c');
    const hrefs = Array.from(toolbar.querySelectorAll('a')).map((a) => a.getAttribute('href'));
    expect(hrefs).toEqual(['/books/b1/transactions', '/books/b1/summary', '/books/b1/planned-expenses', '/books/b1/budgets', '/books/b1/settings']);
    const button = Array.from(toolbar.querySelectorAll('button')).find((b) => b.textContent?.includes('登出'))!;
    button.click();
    fixture.detectChanges();
    expect(logout).toHaveBeenCalled();
  });

  // Q23：帳本名稱是選單，可以切換帳本；非建立者看不到「建立帳本…」（Q15）
  it('book_menu_lists_books_and_marks_current_one', async () => {
    const { openBookMenu } = setup(me(), BOOK);
    const menu = await openBookMenu();

    const items = await menu.getItems();
    expect(await Promise.all(items.map(item => item.getText()))).toEqual(['我的帳本', '另一本']);
    const links = [...document.querySelectorAll<HTMLAnchorElement>('a.book-item')];
    expect(links.map(a => a.getAttribute('href'))).toEqual(['/books/b1/transactions', '/books/b2/transactions']);
    expect(links.map(a => a.getAttribute('aria-current'))).toEqual(['true', null]);
  });

  it('creator_starts_book_creation_from_menu', async () => {
    const { openBookMenu, start } = setup(me({ canCreateBooks: true, remainingBookQuota: 3 }), BOOK);
    const menu = await openBookMenu();
    expect(document.querySelector('.quota-hint')).toBeNull();

    await menu.clickItem({ text: '建立帳本…' });

    expect(start).toHaveBeenCalledTimes(1);
  });

  // Q15：名額用完時停用並說明原因，而不是隱藏
  it('create_is_disabled_with_hint_when_quota_is_used_up', async () => {
    const { openBookMenu } = setup(me({ canCreateBooks: true, remainingBookQuota: 0 }), BOOK);
    const menu = await openBookMenu();

    const [create] = await menu.getItems({ text: '建立帳本…' });
    expect(await create.isDisabled()).toBe(true);
    const hint = document.querySelector('.quota-hint')!;
    expect(hint.textContent!.trim()).toBe('已達 5 本上限');
    expect(hint.id).not.toBe('');
    expect(document.querySelector('.create-book')!.getAttribute('aria-describedby')).toBe(hint.id);
  });
});
