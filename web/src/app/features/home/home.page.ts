import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatListModule } from '@angular/material/list';
import { BookCreation } from '../../core/book/book-creation';
import { SessionService } from '../../core/auth/session.service';
import { NO_BOOKS_CREATOR_MESSAGE, NO_BOOKS_MESSAGE } from '../../core/errors/messages';

@Component({
  selector: 'app-home-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatButtonModule, MatListModule],
  template: `
    @if (books.length === 0) {
      @if (canCreateBooks()) {
        <p class="message">{{ creatorMessage }}</p>
        <button matButton="filled" type="button" class="create-book" (click)="bookCreation.start()">建立帳本</button>
      } @else {
        <p class="message">{{ noBooksMessage }}</p>
      }
    } @else if (books.length > 1) {
      <mat-nav-list class="ledger-sheet books">
        @for (book of books; track book.id) {
          <a mat-list-item [routerLink]="['/books', book.id, 'transactions']">{{ book.name }}</a>
        }
      </mat-nav-list>
    }
  `,
  styles: `
    :host { display: block; max-width: 480px; margin: 24px auto; padding: 0 16px; }
    .message { padding: 16px; }
    .books { padding: 0; }
    .books a { font-family: var(--font-serif); font-size: 1.125rem; border-bottom: 1px solid var(--ledger-rule); }
    .books a:last-child { border-bottom: none; }
    .create-book { margin-left: 16px; }
  `,
})
export class HomePage {
  private readonly session = inject(SessionService);
  protected readonly bookCreation = inject(BookCreation);
  protected readonly noBooksMessage = NO_BOOKS_MESSAGE;
  protected readonly creatorMessage = NO_BOOKS_CREATOR_MESSAGE;
  // authGuard 已確保 me 載入完成
  protected readonly books = this.session.me()!.books;
  // 跟著 me 變：建立時 403 會重新載入 me，被移出名單後按鈕要消失
  protected readonly canCreateBooks = computed(() => this.session.me()?.canCreateBooks ?? false);

  constructor() {
    // 只有一本時直接進入；replaceUrl 避免返回鍵又被導回
    if (this.books.length === 1) {
      void inject(Router).navigate(['/books', this.books[0].id, 'transactions'], { replaceUrl: true });
    }
  }
}
