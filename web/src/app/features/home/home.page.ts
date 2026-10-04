import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink, Router } from '@angular/router';
import { MatListModule } from '@angular/material/list';
import { SessionService } from '../../core/auth/session.service';
import { NO_BOOKS_MESSAGE } from '../../core/errors/messages';

@Component({
  selector: 'app-home-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatListModule],
  template: `
    @if (books.length === 0) {
      <p class="message">{{ noBooksMessage }}</p>
    } @else if (books.length > 1) {
      <mat-nav-list>
        @for (book of books; track book.id) {
          <a mat-list-item [routerLink]="['/books', book.id, 'transactions']">{{ book.name }}</a>
        }
      </mat-nav-list>
    }
  `,
  styles: `.message { padding: 16px; }`,
})
export class HomePage {
  protected readonly noBooksMessage = NO_BOOKS_MESSAGE;
  // authGuard 已確保 me 載入完成
  protected readonly books = inject(SessionService).me()!.books;

  constructor() {
    // 只有一本時直接進入；replaceUrl 避免返回鍵又被導回
    if (this.books.length === 1) {
      void inject(Router).navigate(['/books', this.books[0].id, 'transactions'], { replaceUrl: true });
    }
  }
}
