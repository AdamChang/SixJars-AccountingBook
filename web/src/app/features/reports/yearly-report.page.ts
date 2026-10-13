import { DOCUMENT, NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, map, of, switchMap } from 'rxjs';
import { YearlyLineDto, YearlyReportDto, YearlySectionDto } from '../../core/api/dto';
import { ReportApi } from '../../core/api/report-api';
import { ChartComponent } from '../../shared/chart';
import { budgetMonthOf, parseBudgetMonth } from '../../shared/dates';
import { formatAmount } from '../../shared/money';
import { SECTION_LABELS, VisibleLine, formatShare, readChartPalette, toggle, visibleLines, yearlyStackedBar } from './report-rules';

interface SectionView { section: YearlySectionDto; title: string; rows: VisibleLine<YearlyLineDto>[] }
interface PlanningGroup { key: string; title: string; lines: YearlyLineDto[] }

const MONTH_HEADERS = Array.from({ length: 12 }, (_, i) => `${i + 1}月`);

// 年報（spec §6.3）：12 個月並列、總計、佔收入比、月平均，加上四種支出性質的堆疊長條圖
@Component({
  selector: 'app-yearly-report-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, RouterLink, ChartComponent, NgTemplateOutlet],
  templateUrl: './yearly-report.page.html',
  styleUrl: './yearly-report.page.scss',
})
export class YearlyReportPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(ReportApi);

  protected readonly bookId = toSignal(this.route.paramMap.pipe(map(params => params.get('bookId')!)), {
    requireSync: true,
  });
  // 年報沿用 ?month=，與月報來回切換時停在同一個月份（D12）
  private readonly month = toSignal(
    this.route.queryParamMap.pipe(map(params => parseBudgetMonth(params.get('month')) ?? budgetMonthOf(new Date()))),
    { requireSync: true },
  );
  protected readonly year = computed(() => Math.floor(this.month() / 100));

  // 每個回應都標上它是哪一年的；失敗時 data 為 null（interceptor 已通知）
  private readonly response = toSignal(
    toObservable(computed(() => ({ bookId: this.bookId(), year: this.year() }))).pipe(
      switchMap(({ bookId, year }) => this.api.yearly(bookId, year).pipe(
        map((data): { year: number; data: YearlyReportDto | null } => ({ year, data })),
        catchError(() => of({ year, data: null })),
      )),
    ),
  );
  // 只顯示與網址年份相同的報表：換年後新的還沒回來或載入失敗時，不能在新年份的標題下留著前一年的數字
  protected readonly report = computed(() => {
    const response = this.response();
    return response?.year === this.year() ? response.data ?? undefined : undefined;
  });
  protected readonly failed = computed(() => {
    const response = this.response();
    return response?.year === this.year() && response.data === null;
  });

  private readonly expanded = signal<ReadonlySet<string>>(new Set());
  private readonly palette = readChartPalette(getComputedStyle(inject(DOCUMENT).documentElement));

  protected readonly sections = computed<SectionView[]>(() => {
    const report = this.report();
    if (!report) {
      return [];
    }
    return [report.income, ...report.expenses].map(section => ({
      section, title: SECTION_LABELS[section.kind], rows: visibleLines(section.lines, this.expanded()),
    }));
  });

  protected readonly planningGroups = computed<PlanningGroup[]>(() => {
    const planning = this.report()?.planning;
    if (!planning) {
      return [];
    }
    return [
      { key: 'allocation', title: '入新資金', lines: planning.allocation },
      { key: 'withdrawal', title: '出資金', lines: planning.withdrawal },
      { key: 'return', title: '資金回流', lines: planning.return },
    ].filter(group => group.lines.length > 0);
  });

  protected readonly chart = computed(() => {
    const report = this.report();
    return report ? yearlyStackedBar(report, this.palette) : null;
  });

  protected readonly monthHeaders = MONTH_HEADERS;
  protected readonly formatShare = formatShare;

  protected amount(value: number | null): string {
    return value === null ? '—' : formatAmount(value);
  }

  protected changeYear(delta: number): void {
    void this.router.navigate([], {
      relativeTo: this.route, queryParams: { month: this.month() + delta * 100 }, queryParamsHandling: 'merge',
    });
  }

  protected toggleLine(key: string): void {
    this.expanded.update(expanded => toggle(expanded, key));
  }
}
