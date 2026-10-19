import { TestBed } from '@angular/core/testing';
import { MAT_DATE_LOCALE, provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { By } from '@angular/platform-browser';
import { Subject } from 'rxjs';
import { TransactionDto } from '../../core/api/dto';
import { EditTransactionDialog } from './edit-transaction-dialog';
import { BOOK, transactionDtoFrom } from './testing/book-fixture';
import { TransactionForm } from './transaction-form';

const TX: TransactionDto = transactionDtoFrom(
  {
    kind: 'Expense', date: '2026-03-15', budgetMonth: null, amount: -100, accountId: 'acc-cash',
    counterAccountId: null, categoryId: null, planningFundId: null, loanPrincipal: null, note: null, tagIds: [],
  },
  { id: 'tx-1', version: 3 },
);

async function open() {
  const close = vi.fn();
  const keydown = new Subject<KeyboardEvent>();
  TestBed.configureTestingModule({
    providers: [
      provideNativeDateAdapter(),
      { provide: MAT_DATE_LOCALE, useValue: 'zh-TW' },
      { provide: MAT_DIALOG_DATA, useValue: { book: BOOK, transaction: TX } },
      { provide: MatDialogRef, useValue: { close, keydownEvents: () => keydown } },
    ],
  });
  const fixture = TestBed.createComponent(EditTransactionDialog);
  await fixture.whenStable();
  const form = fixture.debugElement.query(By.directive(TransactionForm)).componentInstance as TransactionForm;
  return { close, keydown, form };
}

describe('EditTransactionDialog', () => {
  it('edits_given_transaction', async () => {
    const { form } = await open();
    expect(form.editing()).toBe(TX);
    expect(form.book()).toBe(BOOK);
  });

  it('closes_as_saved', async () => {
    const { form, close } = await open();
    form.saved.emit(TX);
    expect(close).toHaveBeenCalledExactlyOnceWith('saved');
  });

  it('closes_as_stale', async () => {
    const { form, close } = await open();
    form.stale.emit();
    expect(close).toHaveBeenCalledExactlyOnceWith('stale');
  });

  it('closes_without_result_when_cancelled', async () => {
    const { form, close } = await open();
    form.cancelled.emit();
    expect(close).toHaveBeenCalledExactlyOnceWith();
  });

  it('escape_closes_without_result_other_keys_do_not', async () => {
    const { keydown, close } = await open();
    keydown.next(new KeyboardEvent('keydown', { key: 'Enter' }));
    expect(close).not.toHaveBeenCalled();
    keydown.next(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(close).toHaveBeenCalledExactlyOnceWith();
  });
});
