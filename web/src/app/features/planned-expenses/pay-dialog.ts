import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { map } from 'rxjs';
import { BookDto, PayPlannedExpenseBody, PlannedExpenseDto } from '../../core/api/dto';
import { categoryLabel, loanAccounts } from './planned-expense-rules';

// today 由頁面傳入（toDateString(new Date())），對話框不自己讀時鐘
export interface PayDialogData { planned: PlannedExpenseDto; book: BookDto; today: string }
export type PayDialogResult = Omit<PayPlannedExpenseBody, 'version'>;

// 預定支出付款：貸款性質要選貸款帳戶（唯一時預選）並手動輸入本金（P4 K plan D2、Q20）
@Component({
  selector: 'app-pay-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>付款：{{ label }}</h2>
    <form [formGroup]="form" (ngSubmit)="submit()">
      <mat-dialog-content>
        <mat-form-field>
          <mat-label>日期</mat-label>
          <input matInput type="date" formControlName="date" />
          <mat-error>請輸入日期</mat-error>
        </mat-form-field>
        <mat-form-field>
          <mat-label>金額</mat-label>
          <input matInput type="number" min="0.01" step="0.01" formControlName="amount" cdkFocusInitial />
          <mat-error>請輸入大於 0 的金額</mat-error>
        </mat-form-field>
        <mat-form-field>
          <mat-label>付款帳戶</mat-label>
          <mat-select formControlName="accountId">
            @for (account of accounts; track account.id) {
              <mat-option [value]="account.id">{{ account.name }}</mat-option>
            }
          </mat-select>
          <mat-error>請選擇付款帳戶</mat-error>
        </mat-form-field>
        @if (isLoan) {
          <mat-form-field>
            <mat-label>貸款帳戶</mat-label>
            <mat-select formControlName="loanAccountId">
              @for (account of loans; track account.id) {
                <mat-option [value]="account.id">{{ account.name }}</mat-option>
              }
            </mat-select>
            <mat-error>請選擇貸款帳戶</mat-error>
          </mat-form-field>
          <mat-form-field>
            <mat-label>本金</mat-label>
            <input matInput type="number" min="0" step="0.01" formControlName="loanPrincipal" />
            <mat-error>請輸入本金</mat-error>
          </mat-form-field>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button matButton type="button" mat-dialog-close>取消</button>
        <button matButton="filled" type="submit" [disabled]="invalid()">付款</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    mat-dialog-content { display: flex; flex-direction: column; gap: 4px; }
  `,
})
export class PayDialog {
  protected readonly data = inject<PayDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject<MatDialogRef<PayDialog, PayDialogResult>>(MatDialogRef);

  protected readonly label = categoryLabel(this.data.book, this.data.planned.categoryId);
  protected readonly isLoan =
    this.data.book.categories.find(c => c.id === this.data.planned.categoryId)?.nature === 'Loan';
  // 未封存的帳戶；預定支出目前使用的帳戶即使已封存也保留
  protected readonly accounts = this.data.book.accounts
    .filter(a => a.archivedAt === null || a.id === this.data.planned.accountId);
  protected readonly loans = loanAccounts(this.data.book);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    date: [this.data.today, Validators.required],
    amount: [Math.abs(this.data.planned.estimatedAmount) as number | null, [Validators.required, Validators.min(0.01)]],
    accountId: [this.data.planned.accountId ?? '', Validators.required],
    loanAccountId: [
      this.loans.length === 1 ? this.loans[0].id : (null as string | null), this.isLoan ? Validators.required : null,
    ],
    // 本金不預填（Q20）
    loanPrincipal: [null as number | null, this.isLoan ? [Validators.required, Validators.min(0)] : null],
  });

  protected readonly invalid = toSignal(this.form.statusChanges.pipe(map(status => status !== 'VALID')), {
    initialValue: this.form.invalid,
  });

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    // 非貸款時後端要求貸款帳戶與本金留空
    this.dialogRef.close({
      date: value.date,
      accountId: value.accountId,
      amount: -Number(value.amount),
      loanAccountId: this.isLoan ? value.loanAccountId : null,
      loanPrincipal: this.isLoan ? Number(value.loanPrincipal) : null,
    });
  }
}
