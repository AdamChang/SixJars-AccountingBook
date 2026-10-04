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
  });
});
