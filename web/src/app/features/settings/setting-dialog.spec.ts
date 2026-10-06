import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatInputHarness } from '@angular/material/input/testing';
import { MatSelectHarness } from '@angular/material/select/testing';
import { MatCheckboxHarness } from '@angular/material/checkbox/testing';
import { SettingDialog, SettingDialogData } from './setting-dialog';

async function open(data: SettingDialogData) {
  const close = vi.fn();
  TestBed.configureTestingModule({
    providers: [{ provide: MAT_DIALOG_DATA, useValue: data }, { provide: MatDialogRef, useValue: { close } }],
  });
  const fixture = TestBed.createComponent(SettingDialog);
  await fixture.whenStable();
  const submit = async () => {
    fixture.nativeElement.querySelector('button[type=submit]').click();
    await fixture.whenStable();
  };
  return { close, submit, loader: TestbedHarnessEnvironment.loader(fixture) };
}

describe('SettingDialog', () => {
  it('add_account_returns_all_fields', async () => {
    const { loader, submit, close } = await open({ mode: 'add', path: 'accounts' });
    await (await loader.getHarness(MatInputHarness.with({ selector: '[formControlName=name]' }))).setValue(' 郵局 ');
    const type = await loader.getHarness(MatSelectHarness.with({ selector: '[formControlName=type]' }));
    await type.open();
    await type.clickOptions({ text: '現金' });
    await (await loader.getHarness(MatInputHarness.with({ selector: '[formControlName=openingBalance]' }))).setValue('500');
    await (await loader.getHarness(MatCheckboxHarness)).uncheck();
    await submit();
    expect(close).toHaveBeenCalledWith({ name: '郵局', type: 'Cash', openingBalance: 500, countsAsAvailableCash: false, nature: null });
  });

  it('cash_flag_only_shows_for_cash_type', async () => {
    const { loader } = await open({ mode: 'add', path: 'accounts' });
    const type = await loader.getHarness(MatSelectHarness.with({ selector: '[formControlName=type]' }));
    await type.open();
    await type.clickOptions({ text: '銀行' });
    expect(await loader.getAllHarnesses(MatCheckboxHarness)).toHaveLength(0);
  });

  it('edit_bank_account_shows_only_name', async () => {
    const { loader, submit, close } = await open({ mode: 'edit', path: 'accounts', name: '銀行', accountType: 'Bank', countsAsAvailableCash: false });
    expect(await loader.getAllHarnesses(MatCheckboxHarness)).toHaveLength(0);
    expect(await loader.getAllHarnesses(MatSelectHarness)).toHaveLength(0);
    expect(await (await loader.getHarness(MatInputHarness)).getValue()).toBe('銀行');
    await submit();
    expect(close).toHaveBeenCalledWith({ name: '銀行', countsAsAvailableCash: false, nature: null });
  });

  it('expense_main_category_requires_nature', async () => {
    const { loader, submit, close } = await open({
      mode: 'edit', path: 'categories', name: '飲食', categoryKind: 'Expense', isMain: true, nature: 'Floating',
    });
    const nature = await loader.getHarness(MatSelectHarness.with({ selector: '[formControlName=nature]' }));
    await nature.open();
    await nature.clickOptions({ text: '特別' });
    await submit();
    expect(close).toHaveBeenCalledWith({ name: '飲食', nature: 'Special' });
  });

  it('sub_category_has_no_nature', async () => {
    const { loader, submit, close } = await open({ mode: 'add', path: 'categories', categoryKind: 'Expense', isMain: false });
    expect(await loader.getAllHarnesses(MatSelectHarness)).toHaveLength(0);
    await (await loader.getHarness(MatInputHarness)).setValue('午餐');
    await submit();
    expect(close).toHaveBeenCalledWith({ name: '午餐', nature: null });
  });

  it('blank_name_does_not_close', async () => {
    const { submit, close } = await open({ mode: 'add', path: 'planning-funds' });
    await submit();
    expect(close).not.toHaveBeenCalled();
  });
});
