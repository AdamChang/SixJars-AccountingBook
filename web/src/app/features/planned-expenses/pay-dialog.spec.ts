import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatInputHarness } from '@angular/material/input/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { BookDto, PlannedExpenseDto } from '../../core/api/dto';
import { BOOK } from '../transactions/testing/book-fixture';
import { PayDialog, PayDialogData } from './pay-dialog';

const book: BookDto = {
  ...BOOK,
  categories: [
    ...BOOK.categories,
    { id: 'ins', name: '保險費', kind: 'Expense', nature: 'Fixed', parentId: null, sortOrder: 1, archivedAt: null },
    { id: 'loan', name: '房屋貸款', kind: 'Expense', nature: 'Loan', parentId: null, sortOrder: 2, archivedAt: null },
  ],
};

const planned = (over: Partial<PlannedExpenseDto>): PlannedExpenseDto => ({
  id: 'p1', budgetMonth: 202604, categoryId: 'ins', accountId: 'acc-bank', estimatedAmount: -100, note: null,
  paidTransactionId: null, isPaid: false, version: 3, sourceId: null, ...over,
});

async function open(data: PayDialogData) {
  const close = vi.fn();
  TestBed.configureTestingModule({
    providers: [{ provide: MAT_DIALOG_DATA, useValue: data }, { provide: MatDialogRef, useValue: { close } }],
  });
  const fixture = TestBed.createComponent(PayDialog);
  await fixture.whenStable();
  const submitButton = () => fixture.nativeElement.querySelector('button[type=submit]') as HTMLButtonElement;
  const submit = async () => {
    submitButton().click();
    await fixture.whenStable();
  };
  return { close, submit, submitButton, fixture, loader: TestbedHarnessEnvironment.loader(fixture) };
}

describe('PayDialog', () => {
  it('defaults_to_today_estimated_amount_and_planned_account', async () => {
    const { loader, submit, close } = await open({ planned: planned({}), book, today: '2026-04-05' });
    expect(await (await loader.getHarness(MatInputHarness.with({ selector: '[formControlName=date]' }))).getValue()).toBe('2026-04-05');
    expect(await (await loader.getHarness(MatInputHarness.with({ selector: '[formControlName=amount]' }))).getValue()).toBe('100');
    expect(await (await loader.getHarness(MatSelectHarness.with({ selector: '[formControlName=accountId]' }))).getValueText()).toBe('銀行');
    await submit();
    expect(close).toHaveBeenCalledWith({
      date: '2026-04-05', accountId: 'acc-bank', amount: -100, loanAccountId: null, loanPrincipal: null,
    });
  });

  it('loan_requires_principal_and_preselects_the_only_loan_account', async () => {
    const { loader, submit, submitButton, close } = await open({
      planned: planned({ categoryId: 'loan', estimatedAmount: -3000 }), book, today: '2026-04-05',
    });
    expect(await (await loader.getHarness(MatSelectHarness.with({ selector: '[formControlName=loanAccountId]' }))).getValueText()).toBe('房貸');
    expect(submitButton().disabled).toBe(true);
    await (await loader.getHarness(MatInputHarness.with({ selector: '[formControlName=loanPrincipal]' }))).setValue('2000');
    expect(submitButton().disabled).toBe(false);
    await submit();
    expect(close).toHaveBeenCalledWith({
      date: '2026-04-05', accountId: 'acc-bank', amount: -3000, loanAccountId: 'acc-loan', loanPrincipal: 2000,
    });
  });

  it('non_loan_hides_loan_fields', async () => {
    const { loader } = await open({ planned: planned({}), book, today: '2026-04-05' });
    expect(await loader.getAllHarnesses(MatInputHarness.with({ selector: '[formControlName=loanPrincipal]' }))).toHaveLength(0);
    expect(await loader.getAllHarnesses(MatSelectHarness.with({ selector: '[formControlName=loanAccountId]' }))).toHaveLength(0);
  });

  it('cancel_closes_without_result', async () => {
    const { fixture, close } = await open({ planned: planned({}), book, today: '2026-04-05' });
    (fixture.nativeElement.querySelector('button[mat-dialog-close]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(close).toHaveBeenCalledTimes(1);
    expect(close.mock.calls[0][0]).toBeFalsy();
  });
});
