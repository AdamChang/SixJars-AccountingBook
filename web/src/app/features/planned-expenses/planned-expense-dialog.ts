import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { map } from 'rxjs';
import { BookDto, PlannedExpenseDto, PlannedExpenseInput } from '../../core/api/dto';
import { categoryLabel, categoryOptions } from './planned-expense-rules';

// planned 有值時是修改；budgetMonth 是頁面目前的月份，只在新增時使用
export interface PlannedExpenseDialogData { book: BookDto; budgetMonth: number; planned?: PlannedExpenseDto }

// 不提供月份欄位：新增用頁面月份，修改沿用原月份（P4 K plan D1；也避免搬進鎖定的月份）
@Component({
  selector: 'app-planned-expense-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.planned ? '修改' : '新增' }}預定支出</h2>
    <form [formGroup]="form" (ngSubmit)="submit()">
      <mat-dialog-content>
        <mat-form-field>
          <mat-label>分類</mat-label>
          <mat-select formControlName="categoryId">
            @for (category of categories; track category.id) {
              <mat-option [value]="category.id">{{ label(category.id) }}</mat-option>
            }
          </mat-select>
          <mat-error>請選擇分類</mat-error>
        </mat-form-field>
        <mat-form-field>
          <mat-label>帳戶</mat-label>
          <mat-select formControlName="accountId">
            <mat-option [value]="null">（不指定）</mat-option>
            @for (account of accounts; track account.id) {
              <mat-option [value]="account.id">{{ account.name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field>
          <mat-label>金額</mat-label>
          <input matInput type="number" min="0.01" step="0.01" formControlName="amount" />
          <mat-error>請輸入大於 0 的金額</mat-error>
        </mat-form-field>
        <mat-form-field>
          <mat-label>備註</mat-label>
          <input matInput formControlName="note" maxlength="500" />
        </mat-form-field>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button matButton type="button" mat-dialog-close>取消</button>
        <button matButton="filled" type="submit" [disabled]="invalid()">儲存</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    mat-dialog-content { display: flex; flex-direction: column; gap: 4px; }
  `,
})
export class PlannedExpenseDialog {
  protected readonly data = inject<PlannedExpenseDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject<MatDialogRef<PlannedExpenseDialog, PlannedExpenseInput>>(MatDialogRef);

  private readonly planned = this.data.planned;
  protected readonly categories = categoryOptions(this.data.book, ['Fixed', 'Loan', 'Special'], this.planned?.categoryId);
  protected readonly accounts = this.data.book.accounts
    .filter(a => a.archivedAt === null || a.id === this.planned?.accountId);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    categoryId: [this.planned?.categoryId ?? '', Validators.required],
    accountId: [this.planned?.accountId ?? (null as string | null)],
    amount: [
      this.planned ? Math.abs(this.planned.estimatedAmount) : (null as number | null),
      [Validators.required, Validators.min(0.01)],
    ],
    note: [this.planned?.note ?? ''],
  });

  protected readonly invalid = toSignal(this.form.statusChanges.pipe(map(status => status !== 'VALID')), {
    initialValue: this.form.invalid,
  });

  protected label(categoryId: string): string {
    return categoryLabel(this.data.book, categoryId);
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    this.dialogRef.close({
      budgetMonth: this.planned?.budgetMonth ?? this.data.budgetMonth,
      categoryId: value.categoryId,
      accountId: value.accountId,
      estimatedAmount: -Number(value.amount),
      note: value.note.trim() || null,
    });
  }
}
