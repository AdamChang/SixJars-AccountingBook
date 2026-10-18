import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { BookApi } from '../../core/api/book-api';
import { BookDeletionPreviewDto } from '../../core/api/dto';

export interface DeleteBookDialogData { bookId: string; name: string }

// 刪除帳本的確認：先看統計、可以先下載備份，輸入完全相同的名稱才能刪除（spec F2）。
// 只回傳輸入的名稱，由頁面送出刪除；取消時 afterClosed 回 undefined。
@Component({
  selector: 'app-delete-book-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule],
  template: `
    <h2 mat-dialog-title>刪除帳本「{{ data.name }}」</h2>
    <mat-dialog-content>
      @if (preview(); as p) {
        <p class="warning">刪除後，這本帳本的所有交易、預定支出、預算與設定都會永久移除，無法復原。</p>
        <dl class="counts">
          <dt>開帳日</dt><dd>{{ p.openingDate }}</dd>
          @if (p.lockDate) {
            <dt>鎖帳日</dt><dd>{{ p.lockDate }}</dd>
          }
          <dt>交易</dt><dd>{{ p.transactions }} 筆</dd>
          <dt>預定支出</dt><dd>{{ p.plannedExpenses }} 筆</dd>
          <dt>週期預定支出</dt><dd>{{ p.recurringPlannedExpenses }} 筆</dd>
          <dt>預算</dt><dd>{{ p.categoryBudgets }} 個分類</dd>
          <dt>成員</dt><dd>{{ p.members }} 位</dd>
          <dt>稽核記錄</dt><dd>{{ p.auditEntries }} 筆</dd>
        </dl>
        <a matButton="outlined" class="backup" [href]="backupUrl" download>下載完整備份</a>
        <mat-form-field class="confirm-name">
          <mat-label>輸入帳本名稱以確認</mat-label>
          <input matInput autocomplete="off" [value]="typed()" (input)="typed.set($any($event.target).value)" />
        </mat-form-field>
      } @else if (failed()) {
        <p class="error">無法載入帳本統計，請稍後再試。</p>
      } @else {
        <p class="loading">載入中…</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton type="button" mat-dialog-close>取消</button>
      <button matButton="filled" type="button" class="confirm-delete" [disabled]="!matches()" (click)="confirm()">永久刪除</button>
    </mat-dialog-actions>
  `,
  styles: `
    .counts { display: grid; grid-template-columns: max-content 1fr; gap: 4px 16px; margin: 12px 0; }
    .counts dd { margin: 0; }
    .confirm-name { display: block; margin-top: 16px; }
    .warning, .error { color: var(--mat-sys-error); }
  `,
})
export class DeleteBookDialog {
  protected readonly data = inject<DeleteBookDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<DeleteBookDialog, string>>(MatDialogRef);

  protected readonly preview = signal<BookDeletionPreviewDto | undefined>(undefined);
  protected readonly failed = signal(false);
  protected readonly typed = signal('');
  // 完全相同才算（不 trim，與後端 D2 一致）；統計沒載入成功時不能刪
  protected readonly matches = computed(() => this.preview() !== undefined && this.typed() === this.data.name);
  protected readonly backupUrl = `/api/books/${this.data.bookId}/export/backup.json`;

  constructor() {
    inject(BookApi).deletionPreview(this.data.bookId).pipe(takeUntilDestroyed()).subscribe({
      next: preview => this.preview.set(preview),
      error: () => this.failed.set(true),
    });
  }

  protected confirm(): void {
    this.ref.close(this.typed());
  }
}
