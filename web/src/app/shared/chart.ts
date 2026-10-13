import { ChangeDetectionStrategy, Component, ElementRef, InjectionToken, effect, inject, input, viewChild } from '@angular/core';
import {
  ArcElement, BarController, BarElement, CategoryScale, Chart, ChartConfiguration, DoughnutController, Legend, LinearScale, Tooltip,
} from 'chart.js';

// 只註冊報表用到的 controller 與 element，其餘交給 tree-shaking（spec §6.3）。
// 這個檔案只能被 lazy 的報表頁 import，否則 Chart.js 會進 initial bundle。
Chart.register(DoughnutController, BarController, ArcElement, BarElement, CategoryScale, LinearScale, Tooltip, Legend);

export interface ChartHandle { destroy(): void }
export type ChartFactory = (canvas: HTMLCanvasElement, config: ChartConfiguration) => ChartHandle;

// jsdom 沒有 canvas 2D context：單元測試換成假的 factory，實際繪圖由 Playwright 驗證（P4 M plan D13）
export const CHART_FACTORY = new InjectionToken<ChartFactory>('CHART_FACTORY', {
  providedIn: 'root',
  factory: () => (canvas, config) => new Chart(canvas, config),
});

@Component({
  selector: 'app-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<canvas #canvas role="img" [attr.aria-label]="label()"></canvas>`,
  styles: `
    :host { display: block; position: relative; }
    canvas { max-width: 100%; }
  `,
})
export class ChartComponent {
  readonly config = input.required<ChartConfiguration>();
  // 圖表的文字替代；數字本身在旁邊的表格裡
  readonly label = input.required<string>();

  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private readonly factory = inject(CHART_FACTORY);

  constructor() {
    // config 換掉時先銷毀舊的再建新的（同一個 canvas 不能有兩個 Chart）；元件銷毀時同樣經由 onCleanup 銷毀
    effect(onCleanup => {
      const chart = this.factory(this.canvas().nativeElement, this.config());
      onCleanup(() => chart.destroy());
    });
  }
}
