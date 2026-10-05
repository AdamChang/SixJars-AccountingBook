import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MatButtonHarness } from '@angular/material/button/testing';
import { MatTableHarness } from '@angular/material/table/testing';
import { BookDto, TransactionDto, TransactionInput } from '../../core/api/dto';
import { BOOK, transactionDtoFrom } from './testing/book-fixture';
import { TransactionList } from './transaction-list';

function tx(overrides: Partial<TransactionInput> & { id?: string }): TransactionDto {
  const { id, ...input } = overrides;
  return transactionDtoFrom(
    {
      kind: 'Expense', date: '2026-03-15', budgetMonth: null, amount: -100, accountId: 'acc-cash',
      counterAccountId: null, categoryId: null, planningFundId: null, loanPrincipal: null, note: null, ...input,
    },
    { id: id ?? 'tx-1' },
  );
}

describe('TransactionList', () => {
  async function setup(transactions: TransactionDto[], book: BookDto = BOOK, editingId: string | null = null) {
    const fixture: ComponentFixture<TransactionList> = TestBed.createComponent(TransactionList);
    fixture.componentRef.setInput('transactions', transactions);
    fixture.componentRef.setInput('book', book);
    fixture.componentRef.setInput('editingId', editingId);
    const edit = vi.fn();
    const remove = vi.fn();
    fixture.componentInstance.edit.subscribe(edit);
    fixture.componentInstance.remove.subscribe(remove);
    const loader = TestbedHarnessEnvironment.loader(fixture);
    await fixture.whenStable();
    const rows = async () => (await loader.getHarness(MatTableHarness)).getRows();
    const cellTexts = async () => (await loader.getHarness(MatTableHarness)).getCellTextByIndex();
    return { loader, edit, remove, rows, cellTexts, element: fixture.nativeElement as HTMLElement };
  }

  it('shows_transfer_as_from_to', async () => {
    const { cellTexts } = await setup([
      tx({ kind: 'Transfer', amount: 500, accountId: 'acc-bank', counterAccountId: 'acc-cash' }),
    ]);
    const [row] = await cellTexts();
    expect(row[0]).toBe('03/15');
    expect(row[1]).toBe('轉帳');
    expect(row[2]).toBe('銀行 → 現金');
  });

  it('shows_cash_deposit_as_cash_to_bank', async () => {
    // CashDeposit 的 accountId 是銀行（轉入方），資金實際是 現金 → 銀行
    const { cellTexts } = await setup([
      tx({ kind: 'CashDeposit', amount: 500, accountId: 'acc-bank', counterAccountId: 'acc-cash' }),
    ]);
    expect((await cellTexts())[0][2]).toBe('現金 → 銀行');
  });

  it('shows_only_account_when_no_counter_or_no_flow', async () => {
    const { cellTexts } = await setup([
      tx({ id: 'a', kind: 'FundAllocation', amount: 500, accountId: 'acc-bank', planningFundId: 'fund-travel' }),
      tx({ id: 'b', kind: 'Expense', accountId: 'acc-cash' }),
    ]);
    const rows = await cellTexts();
    expect(rows[0][2]).toBe('銀行');
    expect(rows[1][2]).toBe('現金');
  });

  it('shows_category_with_parent', async () => {
    const { cellTexts } = await setup([
      tx({ id: 'a', categoryId: 'cat-lunch' }),
      tx({ id: 'b', categoryId: 'cat-food' }),
      tx({ id: 'c', categoryId: null }),
    ]);
    expect((await cellTexts()).map(row => row[3])).toEqual(['飲食 / 午餐', '飲食', '']);
  });

  it('formats_negative_amount', async () => {
    const { cellTexts, element } = await setup([
      tx({ id: 'a', amount: -1234.5 }), tx({ id: 'b', kind: 'Income', amount: 2000 }),
    ]);
    const rows = await cellTexts();
    expect(rows[0][4]).toBe('-1,234.5');
    expect(rows[1][4]).toBe('2,000');
    const amounts = element.querySelectorAll('td.mat-column-amount');
    expect(amounts[0].classList).toContain('negative');
    expect(amounts[1].classList).not.toContain('negative');
  });

  it('shows_note', async () => {
    const { cellTexts } = await setup([tx({ note: '便當' })]);
    expect((await cellTexts())[0][5]).toBe('便當');
  });

  it('emits_edit_on_row_click', async () => {
    const dto = tx({});
    const { rows, edit, remove } = await setup([dto]);
    const [row] = await rows();
    await (await row.host()).click();
    expect(edit).toHaveBeenCalledExactlyOnceWith(dto);
    expect(remove).not.toHaveBeenCalled();
  });

  it('delete_button_emits_remove_without_edit', async () => {
    const dto = tx({});
    const { loader, edit, remove } = await setup([dto]);
    await (await loader.getHarness(MatButtonHarness.with({ selector: '.delete' }))).click();
    expect(remove).toHaveBeenCalledExactlyOnceWith(dto);
    expect(edit).not.toHaveBeenCalled();
  });

  it('marks_editing_row', async () => {
    const { element } = await setup([tx({ id: 'a' }), tx({ id: 'b' })], BOOK, 'b');
    const rows = element.querySelectorAll('tr.mat-mdc-row');
    expect(rows[0].classList).not.toContain('editing');
    expect(rows[1].classList).toContain('editing');
  });

  it('keeps_backend_order', async () => {
    const { cellTexts } = await setup([tx({ id: 'a', date: '2026-03-20' }), tx({ id: 'b', date: '2026-03-01' })]);
    expect((await cellTexts()).map(row => row[0])).toEqual(['03/20', '03/01']);
  });

  it('locked_rows_cannot_be_edited_or_deleted', async () => {
    const book: BookDto = { ...BOOK, lockDate: '2026-03-15' };
    const { rows, loader, edit, remove, element } = await setup(
      [tx({ id: 'locked', date: '2026-03-15' }), tx({ id: 'open', date: '2026-03-16' })], book);
    const [lockedRow, openRow] = await rows();
    await (await lockedRow.host()).click();
    expect(edit).not.toHaveBeenCalled();

    const lockIcons = Array.from(element.querySelectorAll('mat-icon')).filter(icon => icon.textContent?.trim() === 'lock');
    expect(lockIcons).toHaveLength(1);

    const buttons = await loader.getAllHarnesses(MatButtonHarness.with({ selector: '.delete' }));
    expect(await buttons[0].isDisabled()).toBe(true);
    expect(await buttons[1].isDisabled()).toBe(false);
    await buttons[0].click();
    expect(remove).not.toHaveBeenCalled();

    await (await openRow.host()).click();
    expect(edit).toHaveBeenCalledTimes(1);
  });

  it('shows_empty_message', async () => {
    const { element } = await setup([]);
    expect(element.textContent).toContain('這個月還沒有交易');
  });
});
