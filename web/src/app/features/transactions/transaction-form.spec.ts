import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HarnessLoader } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MAT_DATE_LOCALE, provideNativeDateAdapter } from '@angular/material/core';
import { MatAutocompleteHarness } from '@angular/material/autocomplete/testing';
import { MatButtonToggleGroupHarness } from '@angular/material/button-toggle/testing';
import { MatExpansionPanelHarness } from '@angular/material/expansion/testing';
import { MatFormFieldHarness } from '@angular/material/form-field/testing';
import { MatInputHarness } from '@angular/material/input/testing';
import { MatMenuHarness } from '@angular/material/menu/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { MatCheckboxHarness } from '@angular/material/checkbox/testing';
import { MatDatepickerInputHarness } from '@angular/material/datepicker/testing';
import { BookDto, TransactionDto, TransactionInput } from '../../core/api/dto';
import { BrowserLocation } from '../../core/browser-location';
import { apiErrorInterceptor } from '../../core/errors/error-interceptor';
import { CONFLICT_MESSAGE, LOCKED_MESSAGE, NOT_FOUND_MESSAGE } from '../../core/errors/messages';
import { Notifier } from '../../core/errors/notifier';
import { budgetMonthOf } from '../../shared/dates';
import { BOOK, transactionDtoFrom } from './testing/book-fixture';
import { MORE_KINDS } from './transaction-rules';
import { TransactionForm } from './transaction-form';

const POST = { method: 'POST', url: `/api/books/${BOOK.id}/transactions` };

// 編輯對象：支出 -100（未退款），刻意用非預設的日期與帳戶，version 7
const EDITING: TransactionDto = transactionDtoFrom(
  {
    kind: 'Expense', date: '2026-03-15', budgetMonth: null, amount: -100, accountId: 'acc-bank',
    counterAccountId: null, categoryId: 'cat-lunch', planningFundId: null, loanPrincipal: null, note: '午餐',
  },
  { id: 'tx-7', version: 7 },
);
const PUT = { method: 'PUT', url: `/api/books/${BOOK.id}/transactions/${EDITING.id}` };

class FormDriver {
  constructor(
    readonly fixture: ComponentFixture<TransactionForm>,
    readonly loader: HarnessLoader,
    readonly httpTesting: HttpTestingController,
    readonly saved: TransactionDto[],
    readonly events: string[],
    readonly notifier: { show: ReturnType<typeof vi.fn> },
  ) {}

  get el(): HTMLElement {
    return this.fixture.nativeElement as HTMLElement;
  }

  field(name: string): Element | null {
    return this.el.querySelector(`[formControlName=${name}]`);
  }

  async selectKind(label: string): Promise<void> {
    const group = await this.loader.getHarness(MatButtonToggleGroupHarness);
    const toggles = await group.getToggles({ text: label });
    if (toggles.length > 0) {
      await toggles[0].check();
      return;
    }
    const menu = await this.loader.getHarness(MatMenuHarness.with({ selector: '.more-kinds' }));
    await menu.clickItem({ text: label });
  }

  async select(controlName: string, optionText: string): Promise<void> {
    const select = await this.loader.getHarness(MatSelectHarness.with({ selector: `[formControlName=${controlName}]` }));
    await select.open();
    await select.clickOptions({ text: optionText });
  }

  async selectOptions(controlName: string): Promise<string[]> {
    const select = await this.loader.getHarness(MatSelectHarness.with({ selector: `[formControlName=${controlName}]` }));
    await select.open();
    const options = await select.getOptions();
    const texts = await Promise.all(options.map(option => option.getText()));
    await select.close();
    return texts;
  }

  async selectedText(controlName: string): Promise<string> {
    const select = await this.loader.getHarness(MatSelectHarness.with({ selector: `[formControlName=${controlName}]` }));
    return select.getValueText();
  }

  input(controlName: string): Promise<MatInputHarness> {
    return this.loader.getHarness(MatInputHarness.with({ selector: `[formControlName=${controlName}]` }));
  }

  async type(controlName: string, text: string): Promise<void> {
    await (await this.input(controlName)).setValue(text);
  }

  async chooseCategory(filterText: string, optionText: string): Promise<void> {
    const autocomplete = await this.loader.getHarness(
      MatAutocompleteHarness.with({ selector: '[formControlName=categoryId]' }),
    );
    await autocomplete.enterText(filterText);
    await autocomplete.selectOption({ text: optionText });
  }

  async errorsOf(label: string): Promise<string[]> {
    const field = await this.loader.getHarness(MatFormFieldHarness.with({ floatingLabelText: label }));
    return field.getTextErrors();
  }

  async fillValidExpense(amount = '100'): Promise<void> {
    await this.select('accountId', '現金');
    await this.type('amount', amount);
    await this.chooseCategory('午', '飲食 / 午餐');
  }

  async submit(): Promise<void> {
    this.el.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await this.fixture.whenStable();
  }

  async edit(dto: TransactionDto | null): Promise<void> {
    this.fixture.componentRef.setInput('editing', dto);
    await this.fixture.whenStable();
  }

  topErrors(): string[] {
    return [...this.el.querySelectorAll('.form-errors li')].map(item => item.textContent!.trim());
  }

  // 送出後以指定的 HTTP 錯誤回應；經過真正的 apiErrorInterceptor，元件收到的是實際的 ApiError
  async submitAndFail(request: { method: string; url: string }, status: number, body: object | null): Promise<void> {
    await this.submit();
    this.httpTesting.expectOne(request).flush(body, { status, statusText: 'Error' });
    await this.fixture.whenStable();
  }

  async submitAndExpectBody(): Promise<TransactionInput> {
    await this.submit();
    const request = this.httpTesting.expectOne(POST);
    const body = request.request.body as TransactionInput;
    request.flush(transactionDtoFrom(body), { status: 201, statusText: 'Created' });
    await this.fixture.whenStable();
    return body;
  }
}

async function setup(book: BookDto = BOOK): Promise<FormDriver> {
  const notifier = { show: vi.fn() };
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(withInterceptors([apiErrorInterceptor])),
      { provide: Notifier, useValue: notifier },
      { provide: BrowserLocation, useValue: { assign: vi.fn(), currentPath: () => '/' } },
      provideHttpClientTesting(),
      provideNativeDateAdapter(),
      { provide: MAT_DATE_LOCALE, useValue: 'zh-TW' },
    ],
  });
  const fixture = TestBed.createComponent(TransactionForm);
  fixture.componentRef.setInput('book', book);
  const saved: TransactionDto[] = [];
  const events: string[] = [];
  fixture.componentInstance.saved.subscribe(dto => saved.push(dto));
  fixture.componentInstance.cancelled.subscribe(() => events.push('cancelled'));
  fixture.componentInstance.stale.subscribe(() => events.push('stale'));
  await fixture.whenStable();
  return new FormDriver(
    fixture,
    TestbedHarnessEnvironment.loader(fixture),
    TestBed.inject(HttpTestingController),
    saved,
    events,
    notifier,
  );
}

describe('TransactionForm', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    vi.useRealTimers();
  });

  it('defaults_to_expense_with_expense_fields_only', async () => {
    const form = await setup();
    const group = await form.loader.getHarness(MatButtonToggleGroupHarness);
    const checked = await group.getToggles({ checked: true });
    expect(await checked[0].getText()).toBe('支出');
    for (const name of ['accountId', 'amount', 'refund', 'categoryId', 'note']) {
      expect(form.field(name), name).not.toBeNull();
    }
    for (const name of ['counterAccountId', 'planningFundId', 'loanPrincipal']) {
      expect(form.field(name), name).toBeNull();
    }
  });

  it('transfer_shows_counter_and_filters_accounts', async () => {
    const form = await setup();
    await form.selectKind('轉帳');
    expect(await form.selectOptions('accountId')).toEqual(['現金', '銀行']);
    expect(form.field('counterAccountId')).not.toBeNull();
    expect(form.field('categoryId')).toBeNull();
  });

  it('more_menu_lists_nine_kinds', async () => {
    const form = await setup();
    const menu = await form.loader.getHarness(MatMenuHarness.with({ selector: '.more-kinds' }));
    await menu.open();
    const items = await menu.getItems();
    expect(items).toHaveLength(9);
    expect(await Promise.all(items.map(item => item.getText()))).toEqual(
      ['提款', '現金存入', '加值', '繳卡費', '新增貸款', '貸款繳款', '入新資金', '出資金', '資金回流'],
    );
    expect(MORE_KINDS).toHaveLength(9);
  });

  it('shows_selected_more_kind_on_more_button', async () => {
    const form = await setup();
    await form.selectKind('提款');
    expect(form.el.querySelector('.more-kinds')!.textContent!.trim()).toBe('提款');
  });

  it('posts_negative_amount_for_expense', async () => {
    const form = await setup();
    await form.fillValidExpense('100');
    const body = await form.submitAndExpectBody();
    expect(body).toMatchObject({
      kind: 'Expense', amount: -100, accountId: 'acc-cash', categoryId: 'cat-lunch',
      budgetMonth: null, counterAccountId: null, planningFundId: null, loanPrincipal: null, note: null,
    });
    expect(form.saved).toHaveLength(1);
  });

  it('posts_positive_amount_for_refund', async () => {
    const form = await setup();
    await form.fillValidExpense('100');
    const refund = await form.loader.getHarness(MatCheckboxHarness.with({ selector: '[formControlName=refund]' }));
    await refund.check();
    const body = await form.submitAndExpectBody();
    expect(body.amount).toBe(100);
    // 存檔後退款回到未勾選
    expect(await refund.isChecked()).toBe(false);
  });

  it('defaults_date_to_local_today_after_midnight', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 0, 1, 0, 30));
    const form = await setup();
    await form.fillValidExpense();
    const body = await form.submitAndExpectBody();
    expect(body.date).toBe('2026-01-01');
  });

  it('hidden_fields_are_sent_as_null_after_switching_kind', async () => {
    const form = await setup();
    await form.selectKind('轉帳');
    await form.select('counterAccountId', '銀行');
    await form.selectKind('支出');
    await form.fillValidExpense();
    const body = await form.submitAndExpectBody();
    expect(body.counterAccountId).toBeNull();
  });

  it('clears_disallowed_account_when_switching_kind', async () => {
    const form = await setup();
    await form.select('accountId', '信用卡');
    await form.selectKind('提款');
    // 切回支出時信用卡又是合法選項；若當初沒清掉，這裡會顯示「信用卡」
    await form.selectKind('支出');
    expect(await form.selectedText('accountId')).toBe('');
  });

  it('keeps_date_kind_account_and_focuses_amount_after_save', async () => {
    const form = await setup();
    // 刻意用非預設的類型與日期：表單 reset 的預設值是「支出」與今天，用預設值測不出遺失
    await form.selectKind('收入');
    const date = await form.loader.getHarness(MatDatepickerInputHarness);
    await date.setValue('2026/3/15');
    const dateText = await date.getValue();
    await form.select('accountId', '銀行');
    await form.type('amount', '250');
    await form.chooseCategory('獎', '薪資 / 獎金');
    await form.type('note', '年終');
    const body = await form.submitAndExpectBody();
    expect(body).toMatchObject({ kind: 'Income', date: '2026-03-15', accountId: 'acc-bank' });

    const group = await form.loader.getHarness(MatButtonToggleGroupHarness);
    const checked = await group.getToggles({ checked: true });
    expect(await checked[0].getText()).toBe('收入');
    expect(await date.getValue()).toBe(dateText);
    expect(await form.selectedText('accountId')).toBe('銀行');
    expect(await (await form.input('amount')).getValue()).toBe('');
    expect(await (await form.input('note')).getValue()).toBe('');
    expect(await (await form.input('categoryId')).getValue()).toBe('');
    expect(document.activeElement).toBe(form.field('amount'));
  });

  it('does_not_send_twice_while_pending', async () => {
    const form = await setup();
    await form.fillValidExpense();
    await form.submit();
    await form.submit();
    const requests = form.httpTesting.match(POST);
    expect(requests).toHaveLength(1);
    requests[0].flush(transactionDtoFrom(requests[0].request.body as TransactionInput));
  });

  it.each(['0', '-5', '1.234', 'abc'])('rejects_invalid_amounts_without_request (%s)', async amount => {
    const form = await setup();
    await form.fillValidExpense(amount);
    await form.submit();
    form.httpTesting.expectNone(POST);
    expect(await form.errorsOf('金額')).toEqual(['金額須大於 0，最多 2 位小數']);
  });

  it('rejects_category_text_not_chosen_from_list', async () => {
    const form = await setup();
    await form.select('accountId', '現金');
    await form.type('amount', '100');
    await form.type('categoryId', '午');
    await form.submit();
    form.httpTesting.expectNone(POST);
    expect(await form.errorsOf('分類')).toEqual(['請從清單選擇分類']);
  });

  it('shows_interest_for_loan_payment', async () => {
    const form = await setup();
    await form.selectKind('貸款繳款');
    await form.select('accountId', '銀行');
    await form.select('counterAccountId', '房貸');
    await form.type('amount', '1000');
    await form.type('loanPrincipal', '800');
    const interest = form.el.querySelector<HTMLInputElement>('input.interest')!;
    expect(interest.value).toBe('200');
    const body = await form.submitAndExpectBody();
    expect(body).toMatchObject({
      kind: 'LoanPayment', amount: 1000, loanPrincipal: 800,
      accountId: 'acc-bank', counterAccountId: 'acc-loan', categoryId: null,
    });
  });

  it('sends_budget_month_only_when_overridden', async () => {
    const form = await setup();
    const panel = await form.loader.getHarness(MatExpansionPanelHarness);
    await panel.expand();
    const budgetMonth = await form.input('budgetMonth');
    expect(await budgetMonth.getValue()).toBe(String(budgetMonthOf(new Date())));

    await form.fillValidExpense();
    expect((await form.submitAndExpectBody()).budgetMonth).toBeNull();

    await form.fillValidExpense();
    await budgetMonth.setValue('202512');
    expect((await form.submitAndExpectBody()).budgetMonth).toBe(202512);

    // 存檔後回到未覆寫
    expect(await budgetMonth.getValue()).toBe(String(budgetMonthOf(new Date())));
    await form.fillValidExpense();
    expect((await form.submitAndExpectBody()).budgetMonth).toBeNull();
  });

  it('budget_month_follows_date_until_edited', async () => {
    const form = await setup();
    const date = await form.loader.getHarness(MatDatepickerInputHarness);
    const budgetMonth = await form.input('budgetMonth');
    await date.setValue('2026/3/15');
    expect(await budgetMonth.getValue()).toBe('202603');
    await budgetMonth.setValue('202512');
    await date.setValue('2026/4/1');
    expect(await budgetMonth.getValue()).toBe('202512');
  });

  it('rejects_invalid_budget_month_without_request', async () => {
    const form = await setup();
    const panel = await form.loader.getHarness(MatExpansionPanelHarness);
    await panel.expand();
    await form.fillValidExpense();
    await (await form.input('budgetMonth')).setValue('202613');
    await form.submit();
    form.httpTesting.expectNone(POST);
    expect(await form.errorsOf('歸屬月份')).toEqual(['請輸入 YYYYMM 格式的月份']);
  });

  describe('editing', () => {
    it('loads_editing_transaction_into_form', async () => {
      const form = await setup();
      await form.edit(EDITING);
      const group = await form.loader.getHarness(MatButtonToggleGroupHarness);
      const checked = await group.getToggles({ checked: true });
      expect(await checked[0].getText()).toBe('支出');
      expect(await (await form.input('amount')).getValue()).toBe('100');
      const refund = await form.loader.getHarness(MatCheckboxHarness.with({ selector: '[formControlName=refund]' }));
      expect(await refund.isChecked()).toBe(false);
      expect(await form.selectedText('accountId')).toBe('銀行');
      expect(await (await form.input('categoryId')).getValue()).toBe('飲食 / 午餐');
      expect(await (await form.input('note')).getValue()).toBe('午餐');
      expect(form.el.querySelector('.editing-badge')?.textContent?.trim()).toBe('編輯中');
      expect(form.el.querySelector('button.cancel')).not.toBeNull();
    });

    it('puts_with_version_when_editing', async () => {
      const form = await setup();
      await form.edit(EDITING);
      await form.type('amount', '120');
      await form.submit();
      form.httpTesting.expectNone(POST);
      const request = form.httpTesting.expectOne(PUT);
      expect(request.request.body).toEqual({
        version: 7,
        input: {
          kind: 'Expense', date: '2026-03-15', budgetMonth: null, amount: -120, accountId: 'acc-bank',
          counterAccountId: null, categoryId: 'cat-lunch', planningFundId: null, loanPrincipal: null, note: '午餐',
        },
      });
      request.flush({ ...EDITING, amount: -120, version: 8 });
      await form.fixture.whenStable();
      expect(form.saved).toHaveLength(1);
    });

    it('returns_to_create_mode_keeping_date_kind_account_when_editing_ends', async () => {
      const form = await setup();
      await form.edit({ ...EDITING, kind: 'Income', amount: 250, categoryId: 'cat-bonus' });
      await form.edit(null);
      expect(form.el.querySelector('.editing-badge')).toBeNull();
      expect(form.el.querySelector('button.cancel')).toBeNull();
      const group = await form.loader.getHarness(MatButtonToggleGroupHarness);
      const checked = await group.getToggles({ checked: true });
      expect(await checked[0].getText()).toBe('收入');
      expect(await form.selectedText('accountId')).toBe('銀行');
      expect(await (await form.input('amount')).getValue()).toBe('');
      expect(await (await form.input('note')).getValue()).toBe('');
      expect(await (await form.input('categoryId')).getValue()).toBe('');
      await form.type('amount', '10');
      await form.chooseCategory('獎', '薪資 / 獎金');
      const body = await form.submitAndExpectBody();
      expect(body).toMatchObject({ kind: 'Income', date: '2026-03-15', accountId: 'acc-bank', amount: 10 });
    });

    it('keeps_budget_month_override_of_editing_transaction', async () => {
      const form = await setup();
      await form.edit({ ...EDITING, budgetMonth: 202602 });
      expect(await (await form.input('budgetMonth')).getValue()).toBe('202602');
      await form.submit();
      const request = form.httpTesting.expectOne(PUT);
      expect((request.request.body as { input: TransactionInput }).input.budgetMonth).toBe(202602);
      request.flush(EDITING);
      await form.fixture.whenStable();
    });

    it('cancel_emits_cancelled', async () => {
      const form = await setup();
      await form.edit(EDITING);
      form.el.querySelector<HTMLButtonElement>('button.cancel')!.click();
      expect(form.events).toEqual(['cancelled']);
      form.httpTesting.expectNone(PUT);
    });

    it('does_not_send_twice_while_pending_when_editing', async () => {
      const form = await setup();
      await form.edit(EDITING);
      await form.submit();
      await form.submit();
      const requests = form.httpTesting.match(PUT);
      expect(requests).toHaveLength(1);
      requests[0].flush(EDITING);
    });
  });

  describe('server errors', () => {
    it('maps_validation_errors_to_fields', async () => {
      const form = await setup();
      await form.selectKind('轉帳');
      await form.select('accountId', '銀行');
      await form.select('counterAccountId', '現金');
      await form.type('amount', '500');
      await form.submitAndFail(POST, 400, { errors: { counterAccountId: ['必須是現金帳戶'] } });
      expect(await form.errorsOf('到')).toEqual(['必須是現金帳戶']);
      expect(form.topErrors()).toEqual([]);

      // 使用者改了該欄位後伺服器錯誤消失，不會卡住下一次送出
      await form.select('counterAccountId', '銀行');
      expect(await form.errorsOf('到')).toEqual([]);
      await form.submit();
      const request = form.httpTesting.expectOne(POST);
      request.flush(transactionDtoFrom(request.request.body as TransactionInput));
      await form.fixture.whenStable();
    });

    it('shows_unmapped_validation_keys_at_top', async () => {
      const form = await setup();
      await form.fillValidExpense();
      await form.submitAndFail(POST, 400, { errors: { foo: ['bar'] } });
      expect(form.topErrors()).toEqual(['foo: bar']);

      // 下一次送出時清掉頂端錯誤
      await form.submit();
      expect(form.topErrors()).toEqual([]);
      const request = form.httpTesting.expectOne(POST);
      request.flush(transactionDtoFrom(request.request.body as TransactionInput));
      await form.fixture.whenStable();
    });

    it('shows_errors_of_hidden_fields_at_top', async () => {
      const form = await setup();
      await form.fillValidExpense();
      // 支出沒有對方帳戶欄位：掛在隱藏欄位上使用者看不到，也會讓表單一直無法送出
      await form.submitAndFail(POST, 400, { errors: { counterAccountId: ['必須是空值'] } });
      expect(form.topErrors()).toEqual(['counterAccountId: 必須是空值']);
      await form.submit();
      const request = form.httpTesting.expectOne(POST);
      request.flush(transactionDtoFrom(request.request.body as TransactionInput));
      await form.fixture.whenStable();
    });

    it('shows_locked_message_for_locked_domain_error', async () => {
      const form = await setup();
      await form.fillValidExpense();
      await form.submitAndFail(POST, 422, { code: 'locked', detail: 'Book is locked' });
      expect(form.topErrors()).toEqual([LOCKED_MESSAGE]);
    });

    it('shows_detail_for_rule_domain_error', async () => {
      const form = await setup();
      await form.fillValidExpense();
      await form.submitAndFail(POST, 422, { code: 'rule', detail: '日期不可早於開帳日' });
      expect(form.topErrors()).toEqual(['日期不可早於開帳日']);
    });

    it('emits_stale_on_conflict', async () => {
      const form = await setup();
      await form.edit(EDITING);
      await form.submitAndFail(PUT, 409, null);
      expect(form.notifier.show).toHaveBeenCalledExactlyOnceWith(CONFLICT_MESSAGE);
      expect(form.events).toEqual(['stale']);
    });

    it('emits_stale_on_not_found', async () => {
      const form = await setup();
      await form.edit(EDITING);
      await form.submitAndFail(PUT, 404, null);
      expect(form.notifier.show).toHaveBeenCalledExactlyOnceWith(NOT_FOUND_MESSAGE);
      expect(form.events).toEqual(['stale']);
    });

    it('keeps_form_values_after_error', async () => {
      const form = await setup();
      await form.fillValidExpense('88');
      await form.type('note', '便當');
      await form.submitAndFail(POST, 422, { code: 'rule', detail: '不行' });
      expect(await form.selectedText('accountId')).toBe('現金');
      expect(await (await form.input('amount')).getValue()).toBe('88');
      expect(await (await form.input('categoryId')).getValue()).toBe('飲食 / 午餐');
      expect(await (await form.input('note')).getValue()).toBe('便當');
      // pending 結束，送出鈕恢復可用
      expect(form.el.querySelector<HTMLButtonElement>('button.submit')!.disabled).toBe(false);
    });
  });

  describe('archived settings', () => {
    const archivedAt = '2026-05-01T00:00:00+00:00';
    const OLD_BANK = {
      id: 'acc-old', name: '舊銀行', type: 'Bank' as const, openingBalance: 0, countsAsAvailableCash: false, sortOrder: 5, archivedAt,
    };
    const OLD_FUND = { id: 'fund-old', name: '舊基金', openingBalance: 0, sortOrder: 1, archivedAt };
    const WITH_ARCHIVED: BookDto = {
      ...BOOK, accounts: [...BOOK.accounts, OLD_BANK], planningFunds: [...BOOK.planningFunds, OLD_FUND],
    };

    it('hides_archived_account_for_new_transaction', async () => {
      const form = await setup(WITH_ARCHIVED);
      expect(await form.selectOptions('accountId')).not.toContain('舊銀行');
    });

    it('keeps_archived_account_of_editing_transaction', async () => {
      const form = await setup(WITH_ARCHIVED);
      await form.edit({ ...EDITING, accountId: 'acc-old' });
      expect(await form.selectedText('accountId')).toBe('舊銀行');
      expect(await form.selectOptions('accountId')).toContain('舊銀行');
    });

    it('hides_archived_planning_fund', async () => {
      const form = await setup(WITH_ARCHIVED);
      await form.selectKind('入新資金');
      expect(await form.selectOptions('planningFundId')).toEqual(['旅遊基金']);
    });
  });
});
