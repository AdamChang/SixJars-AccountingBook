import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TagDto } from '../core/api/dto';
import { tagColorVar } from './tags';

// 標籤的小色塊；封存的標籤仍顯示（既有交易保留），以降低不透明度與 title 區分（F6）
@Component({
  selector: 'app-tag-chip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="tag-chip" [class.archived]="archived()" [attr.title]="archived() ? '（已封存）' : null"
      [style.background-color]="background()" [style.color]="foreground()">{{ tag().name }}</span>
  `,
  styles: `
    :host { display: inline-flex; }
    .tag-chip {
      display: inline-block; padding: 0 8px; border-radius: 999px; font-size: 0.75rem; line-height: 1.5rem;
      white-space: nowrap;
    }
    .archived { opacity: 0.6; }
  `,
})
export class TagChip {
  readonly tag = input.required<TagDto>();

  protected readonly archived = computed(() => this.tag().archivedAt !== null);
  protected readonly background = computed(() => tagColorVar(this.tag().color, 'bg'));
  protected readonly foreground = computed(() => tagColorVar(this.tag().color, 'fg'));
}
