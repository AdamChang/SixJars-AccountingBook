import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { Observable, filter } from 'rxjs';
import { BookDto, TransactionDto } from '../../core/api/dto';
import { TransactionForm } from './transaction-form';

export interface EditTransactionDialogData { book: BookDto; transaction: TransactionDto }
// saved：已更新；stale：版本過期或已被刪除（表單已通知）。兩者都要重新載入清單；取消時為 undefined
export type EditTransactionDialogResult = 'saved' | 'stale';

// 在對話框編輯，清單不必捲動，關閉後停在原本的位置
@Component({
  selector: 'app-edit-transaction-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDialogModule, TransactionForm],
  template: `
    <mat-dialog-content>
      <app-transaction-form
        [book]="data.book"
        [editing]="data.transaction"
        (saved)="dialogRef.close('saved')"
        (cancelled)="dialogRef.close()"
        (stale)="dialogRef.close('stale')" />
    </mat-dialog-content>
  `,
})
export class EditTransactionDialog {
  protected readonly data = inject<EditTransactionDialogData>(MAT_DIALOG_DATA);
  protected readonly dialogRef = inject<MatDialogRef<EditTransactionDialog, EditTransactionDialogResult>>(MatDialogRef);

  constructor() {
    // disableClose 同時擋掉 Esc；只想擋誤觸背景，所以 Esc 自己處理
    this.dialogRef.keydownEvents()
      .pipe(filter(event => event.key === 'Escape'), takeUntilDestroyed())
      .subscribe(() => this.dialogRef.close());
  }
}

export function editTransaction(dialog: MatDialog, data: EditTransactionDialogData): Observable<EditTransactionDialogResult | undefined> {
  return dialog.open<EditTransactionDialog, EditTransactionDialogData, EditTransactionDialogResult>(EditTransactionDialog, {
    data,
    disableClose: true,
    ariaLabel: '編輯交易',
    width: '640px',
    maxWidth: '100vw',
    panelClass: 'edit-transaction-dialog',
  }).afterClosed();
}
