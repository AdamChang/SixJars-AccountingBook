import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { addMonths, formatBudgetMonth } from './dates';

@Component({
  selector: 'app-month-nav',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule],
  template: `
    <button matIconButton type="button" class="step" aria-label="上個月" (click)="step(-1)">‹</button>
    <span class="month">{{ label() }}</span>
    <button matIconButton type="button" class="step" aria-label="下個月" (click)="step(1)">›</button>
  `,
  styles: `
    :host { display: flex; align-items: center; justify-content: center; gap: 12px; }
    .month {
      min-width: 5.5em;
      text-align: center;
      font-family: var(--font-serif);
      font-weight: 700;
      font-size: 1.5rem;
      font-variant-numeric: tabular-nums;
      letter-spacing: 0.06em;
      color: var(--ink);
    }
    .step { font-size: 1.75rem; line-height: 1; color: var(--ink); }
  `,
})
export class MonthNav {
  readonly month = input.required<number>();
  readonly monthChange = output<number>();

  protected readonly label = computed(() => formatBudgetMonth(this.month()));

  protected step(delta: number): void {
    this.monthChange.emit(addMonths(this.month(), delta));
  }
}
