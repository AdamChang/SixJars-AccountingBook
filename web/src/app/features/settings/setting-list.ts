import { ChangeDetectionStrategy, Component, computed, input, linkedSignal, output, signal } from '@angular/core';
import { CdkDrag, CdkDragDrop, CdkDragHandle, CdkDropList } from '@angular/cdk/drag-drop';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { SettingRow, moveRow } from './settings-rules';

// 父層在 API 失敗時呼叫 revert() 還原畫面；一頁有多個清單，所以不讓父層直接操作子元件
export interface ReorderRequest { ids: string[]; revert(): void }

// 一組設定：未封存的列可拖曳或用 ▲▼ 排序（同一條 move 路徑，spec §8），已封存的列收合在下方
@Component({
  selector: 'app-setting-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CdkDropList, CdkDrag, CdkDragHandle, MatButtonModule, MatMenuModule],
  template: `
    <ul class="active" cdkDropList (cdkDropListDropped)="drop($event)">
      @for (row of order(); track row.id; let i = $index, last = $last) {
        <li class="row" cdkDrag>
          <span class="handle" cdkDragHandle aria-hidden="true">⠿</span>
          <span class="label">{{ row.label }}</span>
          <span class="detail">{{ row.detail }}</span>
          <button mat-button class="up" type="button" [disabled]="i === 0" (click)="move(i, i - 1)"
            [attr.aria-label]="row.label + ' 上移'">▲</button>
          <button mat-button class="down" type="button" [disabled]="last" (click)="move(i, i + 1)"
            [attr.aria-label]="row.label + ' 下移'">▼</button>
          <button mat-button type="button" [matMenuTriggerFor]="menu" [attr.data-menu]="row.id"
            [attr.aria-label]="row.label + ' 更多操作'">⋮</button>
          <mat-menu #menu="matMenu">
            <button mat-menu-item type="button" (click)="edit.emit(row.id)">修改</button>
            <button mat-menu-item type="button" (click)="archive.emit(row.id)">封存</button>
            <button mat-menu-item type="button" (click)="remove.emit(row.id)">刪除</button>
          </mat-menu>
        </li>
      }
    </ul>
    @if (archivedRows().length > 0) {
      <button mat-button type="button" class="toggle-archived" (click)="showArchived.update(v => !v)">
        已封存（{{ archivedRows().length }}）{{ showArchived() ? '▴' : '▾' }}
      </button>
      @if (showArchived()) {
        <ul class="archived">
          @for (row of archivedRows(); track row.id) {
            <li class="row">
              <span class="label">{{ row.label }}</span>
              <span class="detail">{{ row.detail }}</span>
              <button mat-button type="button" class="unarchive" (click)="unarchive.emit(row.id)">解除封存</button>
              <button mat-button type="button" class="remove" (click)="remove.emit(row.id)">刪除</button>
            </li>
          }
        </ul>
      }
    }
  `,
  styles: `
    ul { list-style: none; margin: 0; padding: 0; }
    .row { display: flex; align-items: center; gap: 4px; min-height: 44px; border-bottom: 1px solid var(--ledger-rule); }
    .row:last-child { border-bottom: none; }
    .row.cdk-drag-preview { background: var(--ledger-sheet); box-shadow: 0 4px 12px rgb(30 53 80 / 0.18); border-radius: 4px; }
    .row.cdk-drag-placeholder { opacity: 0.3; }
    .label { flex: 1; }
    .detail { color: var(--mat-sys-on-surface-variant); font-size: 0.875rem; }
    .handle { cursor: grab; padding: 0 8px; color: var(--ledger-rule-strong); }
    .archived .row { opacity: 0.6; }
  `,
})
export class SettingList {
  readonly rows = input.required<SettingRow[]>();
  readonly reorder = output<ReorderRequest>();
  readonly edit = output<string>();
  readonly archive = output<string>();
  readonly unarchive = output<string>();
  readonly remove = output<string>();

  // 樂觀更新：拖曳或按 ▲▼ 立刻改畫面；父層重新載入（rows 變成新陣列）或呼叫 revert() 時回到伺服器的順序
  protected readonly order = linkedSignal(() => this.rows().filter(row => !row.archived));
  protected readonly archivedRows = computed(() => this.rows().filter(row => row.archived));
  protected readonly showArchived = signal(false);

  drop(event: CdkDragDrop<SettingRow[]>): void {
    this.move(event.previousIndex, event.currentIndex);
  }

  move(from: number, to: number): void {
    const previous = this.order();
    if (from === to || to < 0 || to >= previous.length) {
      return;
    }
    const byId = new Map(previous.map(row => [row.id, row]));
    const ids = moveRow(previous.map(row => row.id), from, to);
    this.order.set(ids.map(id => byId.get(id)!));
    this.reorder.emit({ ids, revert: () => this.order.set(previous) });
  }
}
