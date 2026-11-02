import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MatButtonHarness } from '@angular/material/button/testing';
import { MatTableHarness } from '@angular/material/table/testing';
import { BookDto, TransactionDto, TransactionInput } from '../../core/api/dto';
import { BOOK, transactionDtoFrom } from './testing/book-fixture';
import { TransactionList } from './transaction-list';

function tx(overrides: Partial<TransactionInput> & { id?: string; createdVia?: string }): TransactionDto {
  const { id, createdVia, ...input } = overrides;
  return transactionDtoFrom(
    {
      kind: 'Expense', date: '2026-03-15', budgetMonth: null, amount: -100, accountId: 'acc-cash',
      counterAccountId: null, categoryId: null, planningFundId: null, loanPrincipal: null, note: null, tagIds: [], ...input,
    },
    { id: id ?? 'tx-1', ...(createdVia === undefined ? {} : { createdVia }) },
  );
}

describe('TransactionList', () => {
  async function setup(
    transactions: TransactionDto[], book: BookDto = BOOK, filtered = false,
  ) {
    const fixture: ComponentFixture<TransactionList> = TestBed.createComponent(TransactionList);
    fixture.componentRef.setInput('filtered', filtered);
    fixture.componentRef.setInput('transactions', transactions);
    fixture.componentRef.setInput('book', book);
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

  it('shows_tag_chips_in_sort_order_before_note_including_archived', async () => {
    const { element } = await setup([tx({ note: '機票', tagIds: ['tag-old', 'tag-trip', 'tag-work'] })]);
    const cell = element.querySelector('td.mat-column-note')!;
    expect([...cell.querySelectorAll('.tag-chip')].map(chip => chip.textContent!.trim())).toEqual(['工作', '旅遊', '舊專案']);
    expect(cell.textContent!.replace(/\s+/g, '')).toBe('工作旅遊舊專案機票');
  });

  it('Agent 記的交易顯示 Claude 標記', async () => {
    const { element } = await setup([
      tx({ id: 'a', createdVia: 'mcp:claude.ai' }),
      tx({ id: 'b' }),
    ]);
    const [first, second] = [...element.querySelectorAll('tr.mat-mdc-row')];
    const badge = first.querySelector('[aria-label="由 Claude 記錄"]');
    expect(badge).not.toBeNull();
    expect(badge!.getAttribute('title')).toBe('mcp:claude.ai');
    expect(second.querySelector('[aria-label="由 Claude 記錄"]')).toBeNull();
  });

  it('一般交易沒有標記', async () => {
    const { element } = await setup([tx({})]);
    expect(element.querySelector('[aria-label="由 Claude 記錄"]')).toBeNull();
  });

  it('empty_message_mentions_filters_only_when_filtered', async () => {
    expect((await setup([])).element.textContent).toContain('這個月還沒有交易');
    const filtered = await setup([], BOOK, true);
    expect(filtered.element.textContent).toContain('這個月沒有符合篩選條件的交易');
  });

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

  it('fund_withdrawal_amount_is_red', async () => {
    const { cellTexts, element } = await setup([
      tx({ id: 'a', kind: 'FundWithdrawal', amount: 500, accountId: 'acc-cash', planningFundId: 'fund-travel' }),
    ]);
    // 出資金的金額是正數，但錢是從財務規劃帳戶流出，比照支出顯示紅字
    expect((await cellTexts())[0][4]).toBe('500');
    expect(element.querySelectorAll('td.mat-column-amount')[0].classList).toContain('negative');
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

  it('balance_adjustment_shows_label_and_signed_amount', async () => {
    const { cellTexts, element } = await setup([
      tx({ id: 'a', kind: 'BalanceAdjustment', amount: -320, accountId: 'acc-bank' }),
      tx({ id: 'b', kind: 'BalanceAdjustment', amount: 150, accountId: 'acc-cash', planningFundId: 'fund-travel' }),
    ]);
    const rows = await cellTexts();
    expect(rows.map(row => row[1])).toEqual(['餘額調整', '餘額調整']);
    // 帳戶的調整顯示帳戶；財務規劃帳戶的調整掛在現金帳戶，顯示被調整的信封
    expect(rows.map(row => row[2])).toEqual(['銀行', '旅遊基金']);
    expect(rows.map(row => row[4])).toEqual(['-320', '150']);
    expect(element.querySelectorAll('td.mat-column-amount')[0].classList).toContain('negative');
  });

  it('balance_adjustment_cannot_be_edited_but_can_be_deleted', async () => {
    const adjustment = tx({ id: 'adj', kind: 'BalanceAdjustment', amount: 100, accountId: 'acc-bank' });
    const { rows, loader, edit, remove, element } = await setup([adjustment]);
    const [row] = await rows();
    await (await row.host()).click();
    expect(edit).not.toHaveBeenCalled();

    const hint = Array.from(element.querySelectorAll('mat-icon')).find(icon => icon.textContent?.trim() === 'edit_off');
    expect(hint?.getAttribute('aria-label')).toContain('餘額調整只能由補匯舊年度產生');
    expect(element.querySelector('tr.mat-mdc-row')!.classList).toContain('not-editable');
    expect(element.querySelector('tr.mat-mdc-row')!.classList).not.toContain('locked');

    const deleteButton = await loader.getHarness(MatButtonHarness.with({ selector: '.delete' }));
    expect(await deleteButton.isDisabled()).toBe(false);
    await deleteButton.click();
    expect(remove).toHaveBeenCalledExactlyOnceWith(adjustment);
  });

  it('locked_balance_adjustment_shows_only_lock', async () => {
    const book: BookDto = { ...BOOK, lockDate: '2026-03-31' };
    const { element } = await setup([tx({ kind: 'BalanceAdjustment', amount: 100, accountId: 'acc-bank' })], book);
    const icons = Array.from(element.querySelectorAll('td.actions mat-icon')).map(icon => icon.textContent?.trim());
    expect(icons).toEqual(['lock', 'delete']);
  });

  it('shows_empty_message', async () => {
    const { element } = await setup([]);
    expect(element.textContent).toContain('這個月還沒有交易');
  });
});
