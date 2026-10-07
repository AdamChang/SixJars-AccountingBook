import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatInputHarness } from '@angular/material/input/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { BookDto, PlannedExpenseDto } from '../../core/api/dto';
import { BOOK } from '../transactions/testing/book-fixture';
import { PlannedExpenseDialog, PlannedExpenseDialogData } from './planned-expense-dialog';

const book: BookDto = {
  ...BOOK,
  categories: [
    ...BOOK.categories,
    { id: 'fix', name: '固定支出', kind: 'Expense', nature: 'Fixed', parentId: null, sortOrder: 1, archivedAt: null },
    { id: 'ins', name: '保險費', kind: 'Expense', nature: 'Fixed', parentId: 'fix', sortOrder: 0, archivedAt: null },
    { id: 'old', name: '舊保單', kind: 'Expense', nature: 'Fixed', parentId: 'fix', sortOrder: 1, archivedAt: '2026-01-01T00:00:00Z' },
  ],
};

const existing: PlannedExpenseDto = {
  id: 'p1', budgetMonth: 202603, categoryId: 'old', accountId: 'acc-bank', estimatedAmount: -300, note: '年繳',
  paidTransactionId: null, isPaid: false, version: 2, sourceId: null,
};

async function open(data: PlannedExpenseDialogData) {
  const close = vi.fn();
  TestBed.configureTestingModule({
    providers: [{ provide: MAT_DIALOG_DATA, useValue: data }, { provide: MatDialogRef, useValue: { close } }],
  });
  const fixture = TestBed.createComponent(PlannedExpenseDialog);
  await fixture.whenStable();
  const submitButton = () => fixture.nativeElement.querySelector('button[type=submit]') as HTMLButtonElement;
  const submit = async () => {
    submitButton().click();
    await fixture.whenStable();
  };
  return { close, submit, submitButton, loader: TestbedHarnessEnvironment.loader(fixture) };
}

describe('PlannedExpenseDialog', () => {
  it('add_returns_input_for_page_month_with_negative_amount', async () => {
    const { loader, submit, close } = await open({ book, budgetMonth: 202604 });
    const category = await loader.getHarness(MatSelectHarness.with({ selector: '[formControlName=categoryId]' }));
    await category.open();
    await category.clickOptions({ text: '固定支出 › 保險費' });
    await (await loader.getHarness(MatInputHarness.with({ selector: '[formControlName=amount]' }))).setValue('250');
    await submit();
    expect(close).toHaveBeenCalledWith({
      budgetMonth: 202604, categoryId: 'ins', accountId: null, estimatedAmount: -250, note: null,
    });
  });

  it('edit_prefills_and_keeps_archived_current_category', async () => {
    const { loader, submit, close } = await open({ book, budgetMonth: 202604, planned: existing });
    const category = await loader.getHarness(MatSelectHarness.with({ selector: '[formControlName=categoryId]' }));
    expect(await category.getValueText()).toBe('固定支出 › 舊保單');
    await category.open();
    expect((await category.getOptions()).length).toBe(3);
    await category.close();
    await submit();
    // 修改沿用原本的月份（D1）
    expect(close).toHaveBeenCalledWith({
      budgetMonth: 202603, categoryId: 'old', accountId: 'acc-bank', estimatedAmount: -300, note: '年繳',
    });
  });

  it('amount_must_be_positive', async () => {
    const { loader, submitButton } = await open({ book, budgetMonth: 202604, planned: existing });
    expect(submitButton().disabled).toBe(false);
    await (await loader.getHarness(MatInputHarness.with({ selector: '[formControlName=amount]' }))).setValue('0');
    expect(submitButton().disabled).toBe(true);
  });
});
