import { TestBed } from '@angular/core/testing';
import { DENIED_MESSAGE } from '../../core/errors/messages';
import { DeniedPage } from './denied.page';

describe('DeniedPage', () => {
  it('denied_links_to_backend_login', () => {
    const fixture = TestBed.createComponent(DeniedPage);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const link = el.querySelector('a')!;
    expect(el.textContent).toContain(DENIED_MESSAGE);
    expect(link.getAttribute('href')).toBe('/auth/login');
    expect(link.hasAttribute('routerLink')).toBe(false);
    expect(link.textContent?.trim()).toBe('重新登入');
    // ADR 0011：建立者沒有帳本也能登入，說明要涵蓋兩種名單（spec F7）
    expect(DENIED_MESSAGE).toBe('登入未完成：可能是此 Google 帳號不是任何帳本的成員、也不在建立者名單，或登入時取消了授權');
  });
});
