import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatInputHarness } from '@angular/material/input/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { BookDto } from '../../core/api/dto';
import { BOOK } from '../transactions/testing/book-fixture';
import { FormBuilder } from '@angular/forms';
import { RecurringDialog, RecurringDialogData, recurrenceValidator } from './recurring-dialog';

const book: BookDto = {
  ...BOOK,
  categories: [
    ...BOOK.categories,
    { id: 'fix', name: '固定支出', kind: 'Expense', nature: 'Fixed', parentId: null, sortOrder: 1, archivedAt: null },
    { id: 'ins', name: '保險費', kind: 'Expense', nature: 'Fixed', parentId: 'fix', sortOrder: 0, archivedAt: null },
  ],
};

async function open(data: RecurringDialogData) {
  const close = vi.fn();
  TestBed.configureTestingModule({
    providers: [{ provide: MAT_DIALOG_DATA, useValue: data }, { provide: MatDialogRef, useValue: { close } }],
  });
  const fixture = TestBed.createComponent(RecurringDialog);
  await fixture.whenStable();
  const loader = TestbedHarnessEnvironment.loader(fixture);
  const submitButton = () => fixture.nativeElement.querySelector('button[type=submit]') as HTMLButtonElement;
  const submit = async () => {
    submitButton().click();
    await fixture.whenStable();
  };
  const fill = async (amount: string) => {
    const category = await loader.getHarness(MatSelectHarness.with({ selector: '[formControlName=categoryId]' }));
    await category.open();
    await category.clickOptions({ text: '固定支出 › 保險費' });
    await (await loader.getHarness(MatInputHarness.with({ selector: '[formControlName=amount]' }))).setValue(amount);
  };
  const select = (name: string) => loader.getHarness(MatSelectHarness.with({ selector: `[formControlName=${name}]` }));
  const input = (name: string) => loader.getHarness(MatInputHarness.with({ selector: `[formControlName=${name}]` }));
  return { close, submit, submitButton, fill, select, input, loader };
}

describe('RecurringDialog', () => {
  it('monthly_returns_empty_months', async () => {
    const { fill, submit, close } = await open({ book, budgetMonth: 202604 });
    await fill('500');
    await submit();
    expect(close).toHaveBeenCalledWith({
      categoryId: 'ins', accountId: null, defaultAmount: -500, note: null,
      frequency: 'Monthly', months: [], startMonth: 202604, endMonth: null,
    });
  });

  it('yearly_requires_at_least_one_month', async () => {
    const { fill, select, submit, submitButton, close } = await open({ book, budgetMonth: 202604 });
    await fill('12000');
    const frequency = await select('frequency');
    await frequency.open();
    await frequency.clickOptions({ text: '每年' });
    expect(submitButton().disabled).toBe(true);
    const months = await select('months');
    await months.open();
    await months.clickOptions({ text: '1 月' });
    await months.clickOptions({ text: '7 月' });
    await months.close();
    expect(submitButton().disabled).toBe(false);
    await submit();
    expect(close).toHaveBeenCalledWith(expect.objectContaining({ frequency: 'Yearly', months: [1, 7] }));
  });

  it('end_month_before_start_is_invalid', async () => {
    const { fill, input, submitButton } = await open({ book, budgetMonth: 202604 });
    await fill('500');
    expect(submitButton().disabled).toBe(false);
    await (await input('endMonth')).setValue('2026-03');
    expect(submitButton().disabled).toBe(true);
    await (await input('endMonth')).setValue('2026-04');
    expect(submitButton().disabled).toBe(false);
  });

  // 不支援 type=month 的瀏覽器會退回文字框，使用者可能輸入無法解析的值；不能默默送出 null
  it('unparseable_month_text_is_invalid', () => {
    const form = (startMonth: string, endMonth: string) => new FormBuilder().group(
      { frequency: 'Monthly', months: [[]], startMonth, endMonth }, { validators: recurrenceValidator });
    expect(form('2026/05', '').errors).toEqual({ startFormat: true });
    expect(form('2026-05', '2026/12').errors).toEqual({ endFormat: true });
    expect(form('2026-05', '').errors).toBeNull();
  });
});
