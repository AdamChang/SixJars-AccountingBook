import { Injectable, inject } from '@angular/core';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { Router } from '@angular/router';
import { BookApi } from '../api/book-api';
import { SessionService } from '../auth/session.service';
import { ApiError } from '../errors/api-error';
import { CREATE_FORBIDDEN_MESSAGE, UNAVAILABLE_MESSAGE } from '../errors/messages';
import { Notifier } from '../errors/notifier';
import type { CreateBookDialog, NewBookInput } from './create-book-dialog';

// 建立帳本的流程（spec F1）：首頁的空狀態與工具列選單共用
@Injectable({ providedIn: 'root' })
export class BookCreation {
  private readonly dialog = inject(MatDialog);
  private readonly api = inject(BookApi);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly notifier = inject(Notifier);

  // 動態載入對話框 chunk 期間為 true：連按「建立帳本」時不會開出第二個對話框
  private opening = false;

  async start(): Promise<void> {
    if (this.opening) return;
    this.opening = true;
    let ref: MatDialogRef<CreateBookDialog, void>;
    try {
      // 對話框與表單模組延遲載入，避免 App／首頁的 initial bundle 超過 angular.json 的 budget（Task 5 的 ng build）
      const dialogModule = await this.loadDialog();
      ref = this.dialog.open<CreateBookDialog, void, void>(dialogModule.CreateBookDialog);
    } catch {
      // chunk 載入失敗（離線、部署換版後舊檔不存在）：提示後結束，不往外丟
      this.notifier.show(UNAVAILABLE_MESSAGE);
      return;
    } finally {
      this.opening = false;
    }
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
              // reload 失敗時 interceptor 已通知，這裡不需要再處理
              this.session.reload().catch(() => undefined);
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

  // 獨立成方法讓 spec 能模擬 chunk 載入失敗（模組層級的 import() 在 Vitest 難以 mock）
  protected loadDialog(): Promise<typeof import('./create-book-dialog')> {
    return import('./create-book-dialog');
  }

  // spec Q21：先重新載入 me（工具列選單與首頁才看得到新帳本），再進入新帳本的帳戶分頁
  private async enter(bookId: string, name: string): Promise<void> {
    // reload 失敗（interceptor 已通知）時帳本仍已建立；bookGuard 會獨立載入該帳本，所以照樣進入
    await this.session.reload().catch(() => undefined);
    await this.router.navigate(['/books', bookId, 'settings'], { queryParams: { tab: 'accounts' } });
    this.notifier.show(`已建立帳本「${name}」`);
  }
}
