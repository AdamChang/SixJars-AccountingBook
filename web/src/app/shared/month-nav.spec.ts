import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MatButtonHarness } from '@angular/material/button/testing';
import { MatMenuHarness } from '@angular/material/menu/testing';
import { MonthNav } from './month-nav';

describe('MonthNav', () => {
  async function setup(month: number) {
    const fixture = TestBed.createComponent(MonthNav);
    fixture.componentRef.setInput('month', month);
    const emitted: number[] = [];
    fixture.componentInstance.monthChange.subscribe(value => emitted.push(value));
    await fixture.whenStable();
    const loader = TestbedHarnessEnvironment.loader(fixture);
    // 選擇面板渲染在 overlay（document 根），不在元件底下
    const rootLoader = TestbedHarnessEnvironment.documentRootLoader(fixture);
    const button = (label: string) => loader.getHarness(MatButtonHarness.with({ selector: `[aria-label="${label}"]` }));
    const panelButton = (selector: string) => rootLoader.getHarness(MatButtonHarness.with({ selector }));
    const menu = () => loader.getHarness(MatMenuHarness.with({ selector: '.month' }));
    const openPicker = async () => (await menu()).open();
    return { emitted, button, panelButton, menu, openPicker, el: fixture.nativeElement as HTMLElement };
  }

  afterEach(() => vi.useRealTimers());

  it('month_nav_navigates_with_query', async () => {
    const { emitted, button, el } = await setup(202601);
    expect(el.textContent).toContain('2026/01');
    await (await button('上個月')).click();
    await (await button('下個月')).click();
    expect(emitted).toEqual([202512, 202602]);
  });

  it('month_nav_picker_jumps_to_month_of_previous_year', async () => {
    const { emitted, panelButton, menu, openPicker } = await setup(202611);
    await openPicker();
    expect(await (await panelButton('.picker-year')).getText()).toBe('2026');

    await (await panelButton('[aria-label="前一年"]')).click();
    // 切年份不關面板、不 emit
    expect(await (await menu()).isOpen()).toBe(true);
    expect(emitted).toEqual([]);
    expect(await (await panelButton('.picker-year')).getText()).toBe('2025');

    await (await panelButton('[aria-label="2025 年 3 月"]')).click();
    expect(emitted).toEqual([202503]);
    expect(await (await menu()).isOpen()).toBe(false);
  });

  it('month_nav_picker_marks_current_selection_and_reopens_on_selected_year', async () => {
    const { panelButton, openPicker, menu } = await setup(202611);
    await openPicker();
    await (await panelButton('[aria-label="前一年"]')).click();
    await (await menu()).close();

    // 重新打開時回到目前選取月份的年份
    await openPicker();
    expect(await (await panelButton('.picker-year')).getText()).toBe('2026');
    const selected = await (await panelButton('[aria-label="2026 年 11 月"]')).host();
    expect(await selected.getAttribute('aria-current')).toBe('true');
  });

  it('month_nav_picker_returns_to_this_month', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 10, 11, 9, 0));
    const { emitted, panelButton, openPicker } = await setup(202503);
    await openPicker();
    await (await panelButton('.picker-today')).click();
    expect(emitted).toEqual([202611]);
  });
});
