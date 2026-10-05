import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { Observable, map } from 'rxjs';

@Component({
  selector: 'app-confirm-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatDialogModule],
  template: `
    <mat-dialog-content>{{ message }}</mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton [mat-dialog-close]="false">取消</button>
      <button matButton="filled" [mat-dialog-close]="true">刪除</button>
    </mat-dialog-actions>
  `,
})
export class ConfirmDialog {
  protected readonly message = inject<string>(MAT_DIALOG_DATA);
}

// 只有明確按「刪除」才是 true；取消、Esc、點背景關閉時 afterClosed 回 false 或 undefined，一律視為 false
export function confirm(dialog: MatDialog, message: string): Observable<boolean> {
  return dialog.open<ConfirmDialog, string, boolean>(ConfirmDialog, { data: message })
    .afterClosed()
    .pipe(map(result => result === true));
}
