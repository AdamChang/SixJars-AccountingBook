import { Injectable, inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Router } from '@angular/router';
import { BookApi } from '../api/book-api';
import { SessionService } from '../auth/session.service';
import { ApiError } from '../errors/api-error';
import { CREATE_FORBIDDEN_MESSAGE } from '../errors/messages';
import { Notifier } from '../errors/notifier';
import { CreateBookDialog, NewBookInput } from './create-book-dialog';

// 建立帳本的流程（spec F1）：首頁的空狀態與工具列選單共用
@Injectable({ providedIn: 'root' })
export class BookCreation {
  private readonly dialog = inject(MatDialog);
  private readonly api = inject(BookApi);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly notifier = inject(Notifier);

  start(): void {
    const ref = this.dialog.open<CreateBookDialog, void, void>(CreateBookDialog);
    const dialog = ref.componentInstance;
    // output 在對話框銷毀時自動結束訂閱
    dialog.submitted.subscribe((input: NewBookInput) => {
      dialog.busy.set(true);
      this.api.create(input).subscribe({
        next: ({ id }) => {
          ref.close();
          void this.enter(id, input.name);
        },
        error: (error: ApiError) => {
          dialog.busy.set(false);
          switch (error.kind) {
            case 'forbidden':
              // 名單可能剛被移除：重新載入 me，canCreateBooks 變成 false，入口跟著消失
              ref.close();
              this.notifier.show(CREATE_FORBIDDEN_MESSAGE);
              void this.session.reload();
              break;
            case 'domain':
            case 'validation':
              // 同名、名額用完、名稱過長：對話框不關，讓使用者修改後再送（spec F3）
              dialog.showError(error);
              break;
            // 離線、XSRF 等已由 interceptor 通知；對話框留著，可以直接再按一次
          }
        },
      });
    });
  }

  // spec Q21：先重新載入 me（工具列選單與首頁才看得到新帳本），再進入新帳本的帳戶分頁
  private async enter(bookId: string, name: string): Promise<void> {
    await this.session.reload();
    await this.router.navigate(['/books', bookId, 'settings'], { queryParams: { tab: 'accounts' } });
    this.notifier.show(`已建立帳本「${name}」`);
  }
}
