import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { addMonths, budgetMonthOf, formatBudgetMonth } from './dates';

const MONTHS = Array.from({ length: 12 }, (_, index) => index + 1);

@Component({
  selector: 'app-month-nav',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatMenuModule],
  template: `
    <button matIconButton type="button" class="step" aria-label="上個月" (click)="step(-1)">‹</button>
    <button
      matButton
      type="button"
      class="month"
      aria-label="選擇年月"
      [matMenuTriggerFor]="picker"
      (menuOpened)="pickerYear.set(selectedYear())"
    >
      {{ label() }}
    </button>
    <button matIconButton type="button" class="step" aria-label="下個月" (click)="step(1)">›</button>

    <mat-menu #picker="matMenu" class="month-picker-panel">
      <div class="picker">
        <!-- 面板內任何 click 都會關閉 mat-menu；切年份要停止冒泡，只有選月份才關閉 -->
        <div class="picker-header">
          <button matIconButton type="button" aria-label="前一年" (click)="$event.stopPropagation(); stepYear(-1)">‹</button>
          <button matButton type="button" class="picker-year" disabled>{{ pickerYear() }}</button>
          <button matIconButton type="button" aria-label="後一年" (click)="$event.stopPropagation(); stepYear(1)">›</button>
        </div>
        <div class="picker-grid">
          @for (m of months; track m) {
            <button
              [matButton]="isSelected(m) ? 'filled' : 'text'"
              type="button"
              [attr.aria-label]="pickerYear() + ' 年 ' + m + ' 月'"
              [attr.aria-current]="isSelected(m) ? 'true' : null"
              (click)="select(pickerYear() * 100 + m)"
            >
              {{ m < 10 ? '0' + m : m }}
            </button>
          }
        </div>
        <button matButton type="button" class="picker-today" (click)="selectThisMonth()">回到本月</button>
      </div>
    </mat-menu>
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
    .picker { display: flex; flex-direction: column; align-items: stretch; gap: 4px; padding: 4px 8px; }
    .picker-header { display: flex; align-items: center; justify-content: space-between; }
    .picker-year {
      font-family: var(--font-serif);
      font-weight: 700;
      font-size: 1.125rem;
      font-variant-numeric: tabular-nums;
      color: var(--ink) !important;
    }
    .picker-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 4px; }
    .picker-grid button { min-width: 0; font-variant-numeric: tabular-nums; }
  `,
})
export class MonthNav {
  readonly month = input.required<number>();
  readonly monthChange = output<number>();

  protected readonly months = MONTHS;
  protected readonly label = computed(() => formatBudgetMonth(this.month()));
  protected readonly selectedYear = computed(() => Math.floor(this.month() / 100));
  // 面板上正在瀏覽的年份；每次打開都重設為目前選取月份的年份
  protected readonly pickerYear = signal(0);

  protected step(delta: number): void {
    this.monthChange.emit(addMonths(this.month(), delta));
  }

  protected stepYear(delta: number): void {
    this.pickerYear.update(year => year + delta);
  }

  protected isSelected(month: number): boolean {
    return this.pickerYear() * 100 + month === this.month();
  }

  protected select(month: number): void {
    if (month !== this.month()) {
      this.monthChange.emit(month);
    }
  }

  // 與各頁面預設月份的算法一致（budgetMonthOf(new Date())）
  protected selectThisMonth(): void {
    this.select(budgetMonthOf(new Date()));
  }
}
