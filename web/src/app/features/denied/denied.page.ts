import { ChangeDetectionStrategy, Component } from '@angular/core';
import { DENIED_MESSAGE } from '../../core/errors/messages';

@Component({
  selector: 'app-denied-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  // 刻意用一般連結：要整頁導向後端 /auth/login，不能走 SPA router
  template: `
    <p class="message">{{ message }}</p>
    <p class="message"><a href="/auth/login">重新登入</a></p>
  `,
  styles: `
    :host { display: block; max-width: 480px; margin: 48px auto; padding: 0 16px; text-align: center; }
    .message { margin: 0 0 12px; }
    a { color: var(--ink); font-weight: 500; }
  `,
})
export class DeniedPage {
  protected readonly message = DENIED_MESSAGE;
}
