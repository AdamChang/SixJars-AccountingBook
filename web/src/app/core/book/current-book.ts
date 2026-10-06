import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { BookApi } from '../api/book-api';
import { BookDto } from '../api/dto';

@Injectable({ providedIn: 'root' })
export class CurrentBook {
  private readonly api = inject(BookApi);
  private readonly bookState = signal<BookDto | undefined>(undefined);
  private cached: { bookId: string; promise: Promise<BookDto> } | undefined;

  readonly book = this.bookState.asReadonly();

  // 只快取目前這一本；同一個 bookId 不重複請求，換帳本才重新請求
  load(bookId: string): Promise<BookDto> {
    if (this.cached?.bookId === bookId) {
      return this.cached.promise;
    }
    const promise = firstValueFrom(this.api.get(bookId)).then(
      (book) => {
        if (this.cached?.promise === promise) {
          this.bookState.set(book);
        }
        return book;
      },
      (error: unknown) => {
        if (this.cached?.promise === promise) {
          this.cached = undefined;
        }
        throw error;
      },
    );
    this.cached = { bookId, promise };
    return promise;
  }

  // 設定變更後呼叫：丟掉快取重新取得，讓記帳頁的選項同步更新；沿用 load() 的競態保護
  reload(bookId: string): Promise<BookDto> {
    this.cached = undefined;
    return this.load(bookId);
  }
}
