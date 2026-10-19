import { ChangeDetectionStrategy, Component, computed, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { toDateString } from '../../shared/dates';
import { ApiError } from '../errors/api-error';

export interface NewBookInput { name: string; openingDate: string }

// 與後端 CreateBookValidator（BookConfiguration 的名稱長度）一致
const NAME_MAX_LENGTH = 100;

// 建立帳本的輸入（spec F2）。不呼叫 API、也不自己關閉：由 BookCreation 送出，
// 失敗時呼叫 showError 讓使用者修改後再送（spec F3：422 不關閉對話框）。
@Component({
  selector: 'app-create-book-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule],
  template: `
    <h2 mat-dialog-title>建立帳本</h2>
    <mat-dialog-content>
      <mat-form-field class="name">
        <mat-label>帳本名稱</mat-label>
        <input matInput autocomplete="off" [attr.maxlength]="nameMaxLength" [value]="name()"
               (input)="name.set($any($event.target).value)" (keydown.enter)="canSubmit() && submit()" />
      </mat-form-field>
      @if (nameError(); as message) {
        <p class="error name-error" role="alert">{{ message }}</p>
      }
      <mat-form-field class="opening-date">
        <mat-label>開帳日</mat-label>
        <input matInput type="date" [value]="openingDate()" (input)="openingDate.set($any($event.target).value)"
               (keydown.enter)="canSubmit() && submit()" />
      </mat-form-field>
      <p class="hint">所有帳戶的期初餘額都以開帳日結束時為準；建立後可以在設定頁新增帳戶。</p>
      @if (serverError(); as message) {
        <p class="error server-error" role="alert">{{ message }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton type="button" mat-dialog-close>取消</button>
      <button matButton="filled" type="button" class="create" [disabled]="!canSubmit()" (click)="submit()">建立</button>
    </mat-dialog-actions>
  `,
  styles: `
    mat-form-field { display: block; }
    .hint { font-size: 0.875rem; opacity: 0.75; }
    .error { margin: 0 0 12px; color: var(--mat-sys-error); }
  `,
})
export class CreateBookDialog {
  readonly submitted = output<NewBookInput>();
  // 送出中由 BookCreation 設定，避免重複送出
  readonly busy = signal(false);

  protected readonly nameMaxLength = NAME_MAX_LENGTH;
  protected readonly name = signal('');
  // 不可用 toISOString：UTC+8 在 00:00–08:00 會得到前一天（shared/dates）
  protected readonly openingDate = signal(toDateString(new Date()));
  protected readonly nameError = signal<string | null>(null);
  protected readonly serverError = signal<string | null>(null);
  protected readonly canSubmit = computed(() => {
    const name = this.name().trim();
    return !this.busy() && name.length > 0 && name.length <= NAME_MAX_LENGTH && this.openingDate() !== '';
  });

  // 送出失敗、對話框不關：名稱的 validation 錯誤顯示在欄位下，其餘（同名、名額用完）顯示在對話框下方
  showError(error: ApiError): void {
    this.nameError.set(null);
    this.serverError.set(null);
    switch (error.kind) {
      case 'domain':
        this.serverError.set(error.message);
        break;
      case 'validation': {
        const nameErrors = error.fieldErrors['name'];
        if (nameErrors?.length) {
          this.nameError.set(nameErrors[0]);
        } else {
          this.serverError.set(Object.values(error.fieldErrors)[0]?.[0] ?? '輸入的資料不正確');
        }
        break;
      }
    }
  }

  protected submit(): void {
    this.nameError.set(null);
    this.serverError.set(null);
    this.submitted.emit({ name: this.name().trim(), openingDate: this.openingDate() });
  }
}
