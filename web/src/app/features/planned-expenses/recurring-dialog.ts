import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { map } from 'rxjs';
import { BookDto, RecurrenceFrequency, RecurringPlannedExpenseDto, RecurringPlannedExpenseInput } from '../../core/api/dto';
import { categoryLabel, categoryOptions, keyToMonthInput, monthInputToKey } from './planned-expense-rules';

// item 有值時是修改；budgetMonth 是頁面目前的月份，新增時當作預設的開始月份
export interface RecurringDialogData { book: BookDto; budgetMonth: number; item?: RecurringPlannedExpenseDto }

// 每年至少選一個月；結束月份不得早於開始月份（後端仍會再檢查）
const recurrenceValidator: ValidatorFn = group => {
  const { frequency, months, startMonth, endMonth } = group.value as {
    frequency: RecurrenceFrequency; months: number[]; startMonth: string; endMonth: string;
  };
  const errors: Record<string, true> = {};
  if (frequency === 'Yearly' && months.length === 0) errors['months'] = true;
  const start = monthInputToKey(startMonth);
  const end = monthInputToKey(endMonth);
  if (start !== null && end !== null && end < start) errors['range'] = true;
  return Object.keys(errors).length > 0 ? errors : null;
};

@Component({
  selector: 'app-recurring-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.item ? '修改' : '新增' }}週期項目</h2>
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
        <mat-form-field>
          <mat-label>頻率</mat-label>
          <mat-select formControlName="frequency">
            <mat-option value="Monthly">每月</mat-option>
            <mat-option value="Yearly">每年</mat-option>
          </mat-select>
        </mat-form-field>
        @if (frequency() === 'Yearly') {
          <mat-form-field>
            <mat-label>月份</mat-label>
            <mat-select formControlName="months" multiple>
              @for (month of monthChoices; track month) {
                <mat-option [value]="month">{{ month }} 月</mat-option>
              }
            </mat-select>
            <mat-hint>至少選一個月</mat-hint>
          </mat-form-field>
        }
        <mat-form-field>
          <mat-label>開始月份</mat-label>
          <input matInput type="month" formControlName="startMonth" />
          <mat-error>請輸入開始月份</mat-error>
        </mat-form-field>
        <mat-form-field>
          <mat-label>結束月份</mat-label>
          <input matInput type="month" formControlName="endMonth" />
          <mat-hint>空白代表沒有結束；已產生過的項目要停用時設這個欄位</mat-hint>
        </mat-form-field>
        @if (form.hasError('range')) {
          <p class="error">結束月份不能早於開始月份</p>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button matButton type="button" mat-dialog-close>取消</button>
        <button matButton="filled" type="submit" [disabled]="invalid()">儲存</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    mat-dialog-content { display: flex; flex-direction: column; gap: 4px; }
    .error { color: var(--mat-sys-error); font-size: 12px; margin: 0; }
  `,
})
export class RecurringDialog {
  protected readonly data = inject<RecurringDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject<MatDialogRef<RecurringDialog, RecurringPlannedExpenseInput>>(MatDialogRef);

  private readonly item = this.data.item;
  protected readonly monthChoices = Array.from({ length: 12 }, (_, i) => i + 1);
  protected readonly categories = categoryOptions(this.data.book, ['Fixed', 'Loan'], this.item?.categoryId);
  protected readonly accounts = this.data.book.accounts
    .filter(a => a.archivedAt === null || a.id === this.item?.accountId);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    categoryId: [this.item?.categoryId ?? '', Validators.required],
    accountId: [this.item?.accountId ?? (null as string | null)],
    amount: [
      this.item ? Math.abs(this.item.defaultAmount) : (null as number | null),
      [Validators.required, Validators.min(0.01)],
    ],
    note: [this.item?.note ?? ''],
    frequency: [this.item?.frequency ?? ('Monthly' as RecurrenceFrequency)],
    months: [this.item?.months ?? ([] as number[])],
    startMonth: [keyToMonthInput(this.item?.startMonth ?? this.data.budgetMonth), Validators.required],
    endMonth: [keyToMonthInput(this.item?.endMonth ?? null)],
  }, { validators: recurrenceValidator });

  protected readonly frequency = toSignal(this.form.controls.frequency.valueChanges, {
    initialValue: this.form.controls.frequency.value,
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
      categoryId: value.categoryId,
      accountId: value.accountId,
      defaultAmount: -Number(value.amount),
      note: value.note.trim() || null,
      frequency: value.frequency,
      // 每月時月份一律為空（D10）
      months: value.frequency === 'Monthly' ? [] : [...value.months].sort((a, b) => a - b),
      startMonth: monthInputToKey(value.startMonth)!,
      endMonth: monthInputToKey(value.endMonth),
    });
  }
}
