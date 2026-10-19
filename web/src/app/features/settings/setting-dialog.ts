import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { AccountType, CategoryKind, ExpenseNature, SettingPath, TagColor } from '../../core/api/dto';
import { TAG_COLORS, TAG_COLOR_LABELS, TAG_NAME_MAX_LENGTH, tagColorVar, textLength } from '../../shared/tags';
import { ACCOUNT_TYPE_LABELS, NATURE_LABELS } from './settings-rules';

export interface SettingDialogData {
  mode: 'add' | 'edit';
  path: SettingPath;
  name?: string;
  accountType?: AccountType;           // 修改帳戶時
  countsAsAvailableCash?: boolean;
  categoryKind?: CategoryKind;         // 分類
  isMain?: boolean;
  nature?: ExpenseNature | null;
  color?: TagColor;                    // 修改標籤時
  defaultColor?: TagColor;             // 新增標籤時預設選的顏色（F3）
}

// 只帶出畫面上有的欄位；nature 不顯示時為 null（呼叫端修改分類時，null 代表不修改性質）
export interface SettingDialogResult {
  name: string; type?: AccountType; openingBalance?: number; countsAsAvailableCash?: boolean; nature: ExpenseNature | null;
  color?: TagColor;
}

// 標籤名稱以 grapheme cluster 計算長度，與後端一致；不能用 maxlength（算的是 UTF-16 字元，F4）
function tagNameLength(control: AbstractControl<string>): ValidationErrors | null {
  return textLength(control.value) > TAG_NAME_MAX_LENGTH ? { tagNameLength: true } : null;
}

const TITLES: Record<SettingPath, string> = { accounts: '帳戶', 'planning-funds': '財務規劃帳戶', categories: '分類', tags: '標籤' };

// 四種設定共用的新增／修改對話框；修改時不提供期初餘額（後端沒有對應的 API）
@Component({
  selector: 'app-setting-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule, MatButtonModule,
  ],
  template: `
    <h2 mat-dialog-title>{{ data.mode === 'add' ? '新增' : '修改' }}{{ title }}</h2>
    <form [formGroup]="form" (ngSubmit)="submit()">
      <mat-dialog-content>
        <mat-form-field>
          <mat-label>名稱</mat-label>
          <input matInput formControlName="name" [attr.maxlength]="isTag ? null : 100" cdkFocusInitial />
          @if (form.controls.name.hasError('tagNameLength')) {
            <mat-error>最多 {{ tagNameMax }} 個字</mat-error>
          } @else {
            <mat-error>請輸入名稱</mat-error>
          }
        </mat-form-field>
        @if (isTag) {
          <fieldset class="colors">
            <legend>顏色</legend>
            <div role="radiogroup" aria-label="顏色">
              @for (color of tagColors; track color) {
                <label class="swatch" [style.background-color]="swatchColor(color)">
                  <input type="radio" formControlName="color" [value]="color" [attr.aria-label]="colorLabels[color]" />
                </label>
              }
            </div>
          </fieldset>
        }
        @if (showType) {
          <mat-form-field>
            <mat-label>類型</mat-label>
            <mat-select formControlName="type">
              @for (type of accountTypes; track type) {
                <mat-option [value]="type">{{ typeLabels[type] }}</mat-option>
              }
            </mat-select>
            <mat-error>請選擇類型</mat-error>
          </mat-form-field>
        }
        @if (showOpening) {
          <mat-form-field>
            <mat-label>期初餘額</mat-label>
            <input matInput type="number" formControlName="openingBalance" />
            <mat-error>請輸入期初餘額</mat-error>
          </mat-form-field>
        }
        @if (showCash()) {
          <mat-checkbox formControlName="countsAsAvailableCash">計入可用現金</mat-checkbox>
        }
        @if (showNature) {
          <mat-form-field>
            <mat-label>支出性質</mat-label>
            <mat-select formControlName="nature">
              @for (nature of natures; track nature) {
                <mat-option [value]="nature">{{ natureLabels[nature] }}</mat-option>
              }
            </mat-select>
            <mat-error>請選擇支出性質</mat-error>
          </mat-form-field>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button matButton type="button" mat-dialog-close>取消</button>
        <button matButton="filled" type="submit">儲存</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    mat-dialog-content { display: flex; flex-direction: column; gap: 4px; }
    .colors { border: none; margin: 0; padding: 0; }
    .colors legend { font: var(--mat-sys-body-small); color: var(--mat-sys-on-surface-variant); padding: 0 0 8px; }
    [role=radiogroup] { display: grid; grid-template-columns: repeat(6, 36px); gap: 8px; }
    .swatch { width: 36px; height: 36px; border-radius: 50%; display: grid; place-items: center; cursor: pointer; }
    /* 原生 radio 保留鍵盤操作（方向鍵切換），外觀換成色塊；選中與聚焦以外框表示 */
    .swatch input { appearance: none; margin: 0; width: 100%; height: 100%; border-radius: 50%; cursor: pointer; }
    .swatch input:checked { box-shadow: 0 0 0 3px var(--ledger-sheet), 0 0 0 5px var(--ink); }
    .swatch input:focus-visible { outline: 2px solid var(--ink); outline-offset: 4px; }
  `,
})
export class SettingDialog {
  protected readonly data = inject<SettingDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject<MatDialogRef<SettingDialog, SettingDialogResult>>(MatDialogRef);

  protected readonly title = TITLES[this.data.path];
  protected readonly typeLabels = ACCOUNT_TYPE_LABELS;
  protected readonly accountTypes = Object.keys(ACCOUNT_TYPE_LABELS) as AccountType[];
  protected readonly natureLabels = NATURE_LABELS;
  protected readonly natures = Object.keys(NATURE_LABELS) as ExpenseNature[];

  protected readonly showType = this.data.mode === 'add' && this.data.path === 'accounts';
  protected readonly showOpening = this.data.mode === 'add' && (this.data.path === 'accounts' || this.data.path === 'planning-funds');
  protected readonly showNature = this.data.path === 'categories' && this.data.categoryKind === 'Expense' && this.data.isMain === true;
  protected readonly isTag = this.data.path === 'tags';
  protected readonly tagColors = TAG_COLORS;
  protected readonly colorLabels = TAG_COLOR_LABELS;
  protected readonly tagNameMax = TAG_NAME_MAX_LENGTH;

  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: [this.data.name ?? '', [Validators.required, Validators.pattern(/\S/), ...(this.isTag ? [tagNameLength] : [])]],
    type: [this.data.accountType ?? (null as AccountType | null), this.showType ? Validators.required : null],
    openingBalance: [0 as number | null, this.showOpening ? Validators.required : null],
    countsAsAvailableCash: [this.data.countsAsAvailableCash ?? true],
    nature: [this.data.nature ?? (null as ExpenseNature | null), this.showNature ? Validators.required : null],
    color: [this.data.color ?? this.data.defaultColor ?? TAG_COLORS[0]],
  });

  // 新增時依目前選的類型；修改時取原本的類型（修改不能換類型）
  private readonly type = toSignal(this.form.controls.type.valueChanges, { initialValue: this.form.controls.type.value });
  protected readonly showCash = computed(() => this.data.path === 'accounts' && this.type() === 'Cash');

  protected swatchColor(color: TagColor): string {
    return tagColorVar(color, 'fg');
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    const result: SettingDialogResult = { name: value.name.trim(), nature: this.showNature ? value.nature : null };
    if (this.showType) {
      result.type = value.type!;
    }
    if (this.showOpening) {
      result.openingBalance = Number(value.openingBalance);
    }
    if (this.data.path === 'accounts') {
      result.countsAsAvailableCash = this.showCash() && value.countsAsAvailableCash;
    }
    if (this.isTag) {
      result.color = value.color;
    }
    this.dialogRef.close(result);
  }
}
