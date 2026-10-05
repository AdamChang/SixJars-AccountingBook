import {
  ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnInit, computed, effect, inject, input, output, signal,
  untracked, viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatExpansionModule, MatExpansionPanel } from '@angular/material/expansion';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatSelectModule } from '@angular/material/select';
import { BookDto, TransactionDto, TransactionKind } from '../../core/api/dto';
import { TransactionApi } from '../../core/api/transaction-api';
import { ApiError } from '../../core/errors/api-error';
import { CONFLICT_MESSAGE, LOCKED_MESSAGE, NOT_FOUND_MESSAGE } from '../../core/errors/messages';
import { Notifier } from '../../core/errors/notifier';
import { budgetMonthOf, parseBudgetMonth } from '../../shared/dates';
import { formatAmount } from '../../shared/money';
import {
  KIND_RULES, KindRule, MORE_KINDS, PRIMARY_KINDS, TransactionFormValue,
  accountsFor, categoryKindOf, categoryOptions, fromTransaction, toTransactionInput,
} from './transaction-rules';

// 金額欄位保留原始字串，避免 type="number" 吃掉小數位數與非法輸入，驗證後才轉 number
const AMOUNT_PATTERN = /^\d+(\.\d{1,2})?$/;

function parseAmountText(text: string): number | null {
  const trimmed = text.trim();
  return AMOUNT_PATTERN.test(trimmed) ? Number(trimmed) : null;
}

const amountValidator: ValidatorFn = control => {
  if ((control.value as string).trim() === '') {
    return { required: true };
  }
  const amount = parseAmountText(control.value as string);
  return amount !== null && amount > 0 ? null : { amount: true };
};

const principalValidator: ValidatorFn = control => {
  if ((control.value as string).trim() === '') {
    return { required: true };
  }
  return parseAmountText(control.value as string) === null ? { principal: true } : null;
};

const budgetMonthValidator: ValidatorFn = control =>
  parseBudgetMonth((control.value as string).trim()) === null ? { budgetMonth: true } : null;

// autocomplete 打了字卻沒選取時，控制項的值是使用者的文字而不是 id
function categoryValidator(allowedIds: string[], required: boolean): ValidatorFn {
  return control => {
    const value = control.value as string;
    if (value === '') {
      return required ? { required: true } : null;
    }
    return allowedIds.includes(value) ? null : { notInList: true };
  };
}

// 後端 validation key（已轉成 body 欄位名稱）與表單欄位的對應；值表示該欄位在此類型下是否顯示
const SERVER_FIELDS: Record<string, (rule: KindRule) => boolean> = {
  date: () => true,
  accountId: () => true,
  counterAccountId: rule => rule.counter !== null,
  categoryId: rule => rule.category !== null,
  planningFundId: rule => rule.planningFund,
  amount: () => true,
  loanPrincipal: rule => rule.loanPrincipal,
  note: () => true,
  budgetMonth: () => true,
};

// resetForm 的輸入：日期、類型、帳戶必給，其餘未給的欄位回到空白
interface ResetValue {
  date: Date | null; kind: TransactionKind; accountId: string; counterAccountId?: string | null;
  amount?: string; refund?: boolean; loanPrincipal?: string; categoryId?: string; planningFundId?: string | null;
  note?: string; budgetMonthOverride?: number | null;
}

function startOfToday(): Date {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), now.getDate());
}

@Component({
  selector: 'app-transaction-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule, MatAutocompleteModule, MatButtonModule, MatButtonToggleModule, MatCheckboxModule,
    MatDatepickerModule, MatExpansionModule, MatInputModule, MatMenuModule, MatSelectModule,
  ],
  templateUrl: './transaction-form.html',
  styleUrl: './transaction-form.scss',
})
export class TransactionForm implements OnInit {
  readonly book = input.required<BookDto>();
  readonly editing = input<TransactionDto | null>(null);
  readonly saved = output<TransactionDto>();
  readonly cancelled = output<void>();
  readonly stale = output<void>();

  private readonly transactionApi = inject(TransactionApi);
  private readonly notifier = inject(Notifier);
  private readonly destroyRef = inject(DestroyRef);
  private readonly formBuilder = inject(NonNullableFormBuilder);
  private readonly amountInput = viewChild.required<ElementRef<HTMLInputElement>>('amountInput');
  private readonly advancedPanel = viewChild.required(MatExpansionPanel);

  protected readonly rules = KIND_RULES;
  protected readonly primaryKinds = PRIMARY_KINDS;
  protected readonly moreKinds = MORE_KINDS;

  private readonly today = startOfToday();
  protected readonly form = this.formBuilder.group({
    date: this.formBuilder.control<Date | null>(this.today, Validators.required),
    kind: this.formBuilder.control<TransactionKind>('Expense'),
    accountId: this.formBuilder.control('', Validators.required),
    counterAccountId: this.formBuilder.control<string | null>(null),
    amount: this.formBuilder.control('', amountValidator),
    refund: this.formBuilder.control(false),
    loanPrincipal: this.formBuilder.control(''),
    categoryId: this.formBuilder.control(''),
    planningFundId: this.formBuilder.control<string | null>(null),
    note: this.formBuilder.control(''),
    // 文字 YYYYMM；未手動修改（pristine）時跟著日期走
    budgetMonth: this.formBuilder.control(String(budgetMonthOf(this.today)), budgetMonthValidator),
  });

  protected readonly pending = signal(false);
  // 對不到欄位的伺服器錯誤，顯示在表單頂端
  protected readonly formErrors = signal<string[]>([]);

  private readonly controls = this.form.controls;
  private readonly kind = toSignal(this.controls.kind.valueChanges, { initialValue: this.controls.kind.value });
  private readonly amountText = toSignal(this.controls.amount.valueChanges, { initialValue: '' });
  private readonly principalText = toSignal(this.controls.loanPrincipal.valueChanges, { initialValue: '' });
  private readonly categoryText = toSignal(this.controls.categoryId.valueChanges, { initialValue: '' });

  protected readonly rule = computed(() => KIND_RULES[this.kind()]);
  protected readonly moreLabel = computed(() => (MORE_KINDS.includes(this.kind()) ? this.rule().label : '更多'));
  protected readonly accountChoices = computed(() => accountsFor(this.book().accounts, this.rule().account));
  protected readonly counterChoices = computed(() => {
    const counter = this.rule().counter;
    return counter ? accountsFor(this.book().accounts, counter) : [];
  });
  private readonly categoryChoices = computed(() =>
    categoryOptions(this.book().categories, categoryKindOf(this.kind())));
  protected readonly filteredCategories = computed(() => {
    const choices = this.categoryChoices();
    const text = this.categoryText().trim();
    // 已選定（值是 id）或尚未輸入時列出全部
    if (text === '' || choices.some(choice => choice.id === text)) {
      return choices;
    }
    return choices.filter(choice => choice.label.includes(text));
  });
  protected readonly interest = computed(() => {
    const amount = parseAmountText(this.amountText());
    const principal = parseAmountText(this.principalText());
    return amount === null || principal === null ? '' : formatAmount(Math.round((amount - principal) * 100) / 100);
  });

  protected readonly categoryLabel = (id: string | null): string =>
    this.categoryChoices().find(choice => choice.id === id)?.label ?? id ?? '';

  constructor() {
    this.controls.kind.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.applyKindRule());
    this.controls.date.valueChanges.pipe(takeUntilDestroyed()).subscribe(date => {
      if (date && this.controls.budgetMonth.pristine) {
        this.controls.budgetMonth.setValue(String(budgetMonthOf(date)));
      }
    });

    let wasEditing = false;
    effect(() => {
      const editing = this.editing();
      untracked(() => {
        if (editing) {
          this.loadTransaction(editing);
        } else if (wasEditing) {
          // 頁面已收到 saved／cancelled 並結束編輯：回到新增狀態，沿用與連續記帳相同的保留規則
          this.resetKeepingDateKindAccount();
        }
      });
      wasEditing = editing !== null;
    });
  }

  ngOnInit(): void {
    // 規則需要 book()，input 要到 ngOnInit 才可讀
    this.applyKindRule();
  }

  protected selectKind(kind: TransactionKind): void {
    this.controls.kind.setValue(kind);
  }

  protected onSubmit(): void {
    if (this.pending()) {
      return;
    }
    this.formErrors.set([]);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      if (this.controls.budgetMonth.invalid) {
        this.advancedPanel().open();
      }
      return;
    }
    this.pending.set(true);
    const input = toTransactionInput(this.toFormValue());
    const editing = this.editing();
    const request = editing
      ? this.transactionApi.update(this.book().id, editing.id, editing.version, input)
      : this.transactionApi.create(this.book().id, input);
    request
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: dto => this.onSaved(dto),
        error: (error: ApiError) => this.onSaveFailed(error),
      });
  }

  private onSaved(dto: TransactionDto): void {
    this.pending.set(false);
    this.saved.emit(dto);
    this.resetKeepingDateKindAccount();
    this.amountInput().nativeElement.focus();
  }

  // 表單內容一律保留；通用錯誤（離線、XSRF、401）已由 interceptor 處理，這裡只處理與這筆交易有關的錯誤
  private onSaveFailed(error: ApiError): void {
    this.pending.set(false);
    switch (error.kind) {
      case 'validation':
        this.showFieldErrors(error.fieldErrors);
        break;
      case 'domain':
        this.formErrors.set([error.code === 'locked' ? LOCKED_MESSAGE : error.message]);
        break;
      case 'conflict':
        this.notifier.show(CONFLICT_MESSAGE);
        this.stale.emit();
        break;
      case 'notFound':
        this.notifier.show(NOT_FOUND_MESSAGE);
        this.stale.emit();
        break;
    }
  }

  // server 錯誤會在使用者修改該欄位時由重新驗證清掉；隱藏欄位若掛上錯誤會讓表單一直 invalid，所以改列在頂端
  private showFieldErrors(fieldErrors: Record<string, string[]>): void {
    const rule = this.rule();
    const unmapped: string[] = [];
    for (const [key, messages] of Object.entries(fieldErrors)) {
      if (SERVER_FIELDS[key]?.(rule)) {
        const control = this.form.get(key)!;
        control.setErrors({ server: messages[0] });
        control.markAsTouched();
        if (key === 'budgetMonth') {
          this.advancedPanel().open();
        }
      } else {
        unmapped.push(...messages.map(message => `${key}: ${message}`));
      }
    }
    this.formErrors.set(unmapped);
  }

  private loadTransaction(dto: TransactionDto): void {
    const value = fromTransaction(dto);
    this.resetForm({
      ...value,
      categoryId: value.categoryId ?? '',
      amount: String(value.amount),
      loanPrincipal: value.loanPrincipal === null ? '' : String(value.loanPrincipal),
    });
  }

  // 連續記帳與結束編輯共用：保留日期、類型、帳戶
  private resetKeepingDateKindAccount(): void {
    const { date, kind, accountId } = this.form.getRawValue();
    this.resetForm({ date, kind, accountId });
  }

  // 唯一的重設入口：未指定的欄位回到空白，歸屬月份未覆寫時跟著日期
  private resetForm({ budgetMonthOverride = null, ...value }: ResetValue): void {
    this.formErrors.set([]);
    this.form.reset({
      counterAccountId: null,
      amount: '',
      refund: false,
      loanPrincipal: '',
      categoryId: '',
      planningFundId: null,
      note: '',
      ...value,
      budgetMonth: String(budgetMonthOverride ?? budgetMonthOf(value.date ?? this.today)),
    });
    if (budgetMonthOverride !== null) {
      // 編輯的那筆有覆寫：之後改日期時不再跟著走
      this.controls.budgetMonth.markAsDirty();
    }
    this.applyKindRule();
  }

  // 切換類型：清掉新類型不允許的選擇，並依規則調整必填
  private applyKindRule(): void {
    const kind = this.controls.kind.value;
    const rule = KIND_RULES[kind];
    const accounts = this.book().accounts;
    const ids = (list: { id: string }[]) => list.map(item => item.id);

    if (!ids(accountsFor(accounts, rule.account)).includes(this.controls.accountId.value)) {
      this.controls.accountId.setValue('');
    }
    const counterIds = rule.counter ? ids(accountsFor(accounts, rule.counter)) : [];
    const counterId = this.controls.counterAccountId.value;
    if (counterId !== null && !counterIds.includes(counterId)) {
      this.controls.counterAccountId.setValue(null);
    }
    const categoryIds = rule.category ? ids(categoryOptions(this.book().categories, categoryKindOf(kind))) : [];
    if (!categoryIds.includes(this.controls.categoryId.value)) {
      this.controls.categoryId.setValue('');
    }

    this.controls.counterAccountId.setValidators(rule.counter?.required ? Validators.required : null);
    this.controls.categoryId.setValidators(rule.category ? categoryValidator(categoryIds, rule.category.required) : null);
    this.controls.planningFundId.setValidators(rule.planningFund ? Validators.required : null);
    this.controls.loanPrincipal.setValidators(rule.loanPrincipal ? principalValidator : null);
    for (const control of [
      this.controls.counterAccountId, this.controls.categoryId, this.controls.planningFundId, this.controls.loanPrincipal,
    ]) {
      control.updateValueAndValidity();
    }
  }

  // 僅在表單通過驗證後呼叫；規則外欄位交給 toTransactionInput 轉成 null
  private toFormValue(): TransactionFormValue {
    const raw = this.form.getRawValue();
    const date = raw.date!;
    const budgetMonth = parseBudgetMonth(raw.budgetMonth.trim());
    return {
      kind: raw.kind,
      date,
      accountId: raw.accountId,
      counterAccountId: raw.counterAccountId,
      categoryId: raw.categoryId === '' ? null : raw.categoryId,
      planningFundId: raw.planningFundId,
      amount: parseAmountText(raw.amount)!,
      refund: raw.refund,
      loanPrincipal: parseAmountText(raw.loanPrincipal),
      note: raw.note,
      budgetMonthOverride: budgetMonth === budgetMonthOf(date) ? null : budgetMonth,
    };
  }
}
