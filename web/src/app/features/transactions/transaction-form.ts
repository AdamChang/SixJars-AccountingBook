import {
  ChangeDetectionStrategy, Component, DestroyRef, ElementRef, OnInit, computed, inject, input, output, signal, viewChild,
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
import { budgetMonthOf, parseBudgetMonth } from '../../shared/dates';
import { formatAmount } from '../../shared/money';
import {
  KIND_RULES, MORE_KINDS, PRIMARY_KINDS, TransactionFormValue,
  accountsFor, categoryKindOf, categoryOptions, toTransactionInput,
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
  readonly saved = output<TransactionDto>();

  private readonly transactionApi = inject(TransactionApi);
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
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      if (this.controls.budgetMonth.invalid) {
        this.advancedPanel().open();
      }
      return;
    }
    this.pending.set(true);
    this.transactionApi.create(this.book().id, toTransactionInput(this.toFormValue()))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: dto => this.onSaved(dto),
        error: (error: ApiError) => this.onSaveFailed(error),
      });
  }

  private onSaved(dto: TransactionDto): void {
    this.pending.set(false);
    this.saved.emit(dto);
    // 連續記帳：保留日期、類型、帳戶
    const { date, kind, accountId } = this.form.getRawValue();
    this.resetForm({ date, kind, accountId });
    this.amountInput().nativeElement.focus();
  }

  // 表單內容一律保留；通用錯誤（離線、XSRF、401）已由 interceptor 處理。H4 在此把錯誤對回欄位與訊息。
  private onSaveFailed(_error: ApiError): void {
    this.pending.set(false);
  }

  // 唯一的重設入口：未指定的欄位回到空白，歸屬月份回到「未覆寫」
  private resetForm(value: { date: Date | null; kind: TransactionKind; accountId: string }): void {
    this.form.reset({
      counterAccountId: null,
      amount: '',
      refund: false,
      loanPrincipal: '',
      categoryId: '',
      planningFundId: null,
      note: '',
      ...value,
      budgetMonth: String(budgetMonthOf(value.date ?? this.today)),
    });
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
