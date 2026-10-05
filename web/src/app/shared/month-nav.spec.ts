import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { MatButtonHarness } from '@angular/material/button/testing';
import { MonthNav } from './month-nav';

describe('MonthNav', () => {
  async function setup(month: number) {
    const fixture = TestBed.createComponent(MonthNav);
    fixture.componentRef.setInput('month', month);
    const emitted: number[] = [];
    fixture.componentInstance.monthChange.subscribe(value => emitted.push(value));
    await fixture.whenStable();
    const loader = TestbedHarnessEnvironment.loader(fixture);
    const button = (label: string) => loader.getHarness(MatButtonHarness.with({ selector: `[aria-label="${label}"]` }));
    return { emitted, button, el: fixture.nativeElement as HTMLElement };
  }

  it('month_nav_navigates_with_query', async () => {
    const { emitted, button, el } = await setup(202601);
    expect(el.textContent).toContain('2026/01');
    await (await button('上個月')).click();
    await (await button('下個月')).click();
    expect(emitted).toEqual([202512, 202602]);
  });
});
