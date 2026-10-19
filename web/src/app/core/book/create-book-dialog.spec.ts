import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MatButtonHarness } from '@angular/material/button/testing';
import { MatDialogRef } from '@angular/material/dialog';
import { MatInputHarness } from '@angular/material/input/testing';
import { CreateBookDialog, NewBookInput } from './create-book-dialog';

async function open() {
  TestBed.configureTestingModule({ providers: [{ provide: MatDialogRef, useValue: { close: vi.fn() } }] });
  const fixture = TestBed.createComponent(CreateBookDialog);
  const submitted: NewBookInput[] = [];
  fixture.componentInstance.submitted.subscribe(input => submitted.push(input));
  await fixture.whenStable();
  const loader = TestbedHarnessEnvironment.loader(fixture);
  return {
    fixture, submitted, el: fixture.nativeElement as HTMLElement,
    createButton: () => loader.getHarness(MatButtonHarness.with({ text: '建立' })),
    nameInput: () => loader.getHarness(MatInputHarness.with({ selector: '.name input' })),
    dateInput: () => loader.getHarness(MatInputHarness.with({ selector: '.opening-date input' })),
  };
}

describe('CreateBookDialog', () => {
  afterEach(() => vi.useRealTimers());

  it('defaults_opening_date_to_local_today_and_requires_a_name', async () => {
    // 台北 07:30 是 UTC 前一天 23:30：用 toISOString 會得到 2026-10-18
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 9, 19, 7, 30));
    const { createButton, nameInput, dateInput } = await open();

    expect(await (await dateInput()).getValue()).toBe('2026-10-19');
    expect(await (await createButton()).isDisabled()).toBe(true);

    await (await nameInput()).setValue('   ');
    expect(await (await createButton()).isDisabled()).toBe(true);

    await (await nameInput()).setValue(' 家庭帳本 ');
    expect(await (await createButton()).isDisabled()).toBe(false);
  });

  it('create_emits_trimmed_name_and_opening_date', async () => {
    const { createButton, nameInput, dateInput, submitted } = await open();
    await (await nameInput()).setValue(' 家庭帳本 ');
    await (await dateInput()).setValue('2026-01-01');

    await (await createButton()).click();

    expect(submitted).toEqual([{ name: '家庭帳本', openingDate: '2026-01-01' }]);
  });

  it('busy_disables_create_to_prevent_double_submit', async () => {
    const { fixture, createButton, nameInput } = await open();
    await (await nameInput()).setValue('家庭帳本');

    fixture.componentInstance.busy.set(true);
    await fixture.whenStable();

    expect(await (await createButton()).isDisabled()).toBe(true);
  });

  it('show_error_displays_backend_messages_and_keeps_input', async () => {
    const { fixture, el, nameInput } = await open();
    await (await nameInput()).setValue('家庭帳本');

    fixture.componentInstance.showError({ kind: 'domain', code: 'duplicate-name', message: '你已經有名稱為「家庭帳本」的帳本。' });
    await fixture.whenStable();
    expect(el.querySelector('.server-error')!.textContent).toContain('你已經有名稱為「家庭帳本」的帳本。');
    expect(await (await nameInput()).getValue()).toBe('家庭帳本');

    fixture.componentInstance.showError({ kind: 'validation', fieldErrors: { name: ['名稱不可超過 100 個字。'] } });
    await fixture.whenStable();
    expect(el.querySelector('.name-error')!.textContent).toContain('名稱不可超過 100 個字。');
    expect(el.querySelector('.server-error')).toBeNull();
  });
});
