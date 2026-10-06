import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CdkDragDrop } from '@angular/cdk/drag-drop';
import { ReorderRequest, SettingList } from './setting-list';
import { SettingRow } from './settings-rules';

const ROWS: SettingRow[] = [
  { id: 'a', label: '甲', detail: '', archived: false },
  { id: 'b', label: '乙', detail: '', archived: false },
  { id: 'c', label: '丙', detail: '', archived: false },
  { id: 'z', label: '舊', detail: '', archived: true },
];

describe('SettingList', () => {
  let fixture: ComponentFixture<SettingList>;
  let el: HTMLElement;
  const reorders: ReorderRequest[] = [];
  const unarchived: string[] = [];

  beforeEach(async () => {
    reorders.length = 0;
    unarchived.length = 0;
    fixture = TestBed.createComponent(SettingList);
    fixture.componentRef.setInput('rows', ROWS);
    fixture.componentInstance.reorder.subscribe(r => reorders.push(r));
    fixture.componentInstance.unarchive.subscribe(id => unarchived.push(id));
    await fixture.whenStable();
    el = fixture.nativeElement;
  });

  const labels = () => [...el.querySelectorAll('.active .label')].map(n => n.textContent!.trim());
  const button = (row: number, cls: 'up' | 'down') =>
    el.querySelectorAll<HTMLElement>('.active .row')[row].querySelector<HTMLButtonElement>(`button.${cls}`)!;

  it('up_button_moves_row_and_emits_ids', async () => {
    button(1, 'up').click();
    await fixture.whenStable();
    expect(labels()).toEqual(['乙', '甲', '丙']);
    expect(reorders.map(r => r.ids)).toEqual([['b', 'a', 'c']]);
  });

  it('first_up_and_last_down_are_disabled', () => {
    expect(button(0, 'up').disabled).toBe(true);
    expect(button(2, 'down').disabled).toBe(true);
    expect(button(1, 'down').disabled).toBe(false);
  });

  it('drop_uses_same_path', async () => {
    fixture.componentInstance.drop({ previousIndex: 0, currentIndex: 2 } as CdkDragDrop<SettingRow[]>);
    await fixture.whenStable();
    expect(labels()).toEqual(['乙', '丙', '甲']);
    expect(reorders.map(r => r.ids)).toEqual([['b', 'c', 'a']]);
  });

  it('drop_in_place_emits_nothing', () => {
    fixture.componentInstance.drop({ previousIndex: 1, currentIndex: 1 } as CdkDragDrop<SettingRow[]>);
    expect(reorders).toEqual([]);
  });

  it('revert_restores_previous_order', async () => {
    button(2, 'up').click();
    await fixture.whenStable();
    reorders[0].revert();
    await fixture.whenStable();
    expect(labels()).toEqual(['甲', '乙', '丙']);
  });

  it('archived_rows_are_collapsed_until_toggled', async () => {
    expect(el.querySelector('.archived')).toBeNull();
    el.querySelector<HTMLButtonElement>('button.toggle-archived')!.click();
    await fixture.whenStable();
    expect(el.querySelector('.archived .label')?.textContent?.trim()).toBe('舊');
    el.querySelector<HTMLButtonElement>('.archived button.unarchive')!.click();
    expect(unarchived).toEqual(['z']);
  });

  it('new_rows_input_resets_local_order', async () => {
    button(1, 'up').click();
    fixture.componentRef.setInput('rows', [...ROWS]);
    await fixture.whenStable();
    expect(labels()).toEqual(['甲', '乙', '丙']);
  });
});
