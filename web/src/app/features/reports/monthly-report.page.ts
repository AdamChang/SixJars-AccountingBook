import { DOCUMENT } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { EMPTY, catchError, map, switchMap } from 'rxjs';
import { ReportLineDto, ReportSectionDto } from '../../core/api/dto';
import { ReportApi } from '../../core/api/report-api';
import { ChartComponent } from '../../shared/chart';
import { budgetMonthOf, parseBudgetMonth } from '../../shared/dates';
import { formatAmount } from '../../shared/money';
import { MonthNav } from '../../shared/month-nav';
import { SECTION_LABELS, VisibleLine, floatingDoughnut, natureDoughnut, readChartPalette, toggle, visibleLines } from './report-rules';

interface SectionView { section: ReportSectionDto; title: string; rows: VisibleLine<ReportLineDto>[] }

// 月報（spec §6.3）：頁首摘要、兩張甜甜圈、可展開的分類樹與財務規劃表
@Component({
  selector: 'app-monthly-report-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MonthNav, MatButtonModule, RouterLink, ChartComponent],
  templateUrl: './monthly-report.page.html',
  styleUrl: './monthly-report.page.scss',
})
export class MonthlyReportPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(ReportApi);

  protected readonly bookId = toSignal(this.route.paramMap.pipe(map(params => params.get('bookId')!)), {
    requireSync: true,
  });
  // 網址的 month 不合法時退回今天所在的月份
  protected readonly month = toSignal(
    this.route.queryParamMap.pipe(map(params => parseBudgetMonth(params.get('month')) ?? budgetMonthOf(new Date()))),
    { requireSync: true },
  );

  // 載入失敗時 interceptor 已通知，保留上一次的報表
  protected readonly report = toSignal(
    toObservable(computed(() => ({ bookId: this.bookId(), month: this.month() }))).pipe(
      switchMap(({ bookId, month }) => this.api.monthly(bookId, month).pipe(catchError(() => EMPTY))),
    ),
  );

  // 換月時不清空：同一個分類在不同月份通常還在，保留展開狀態比較順
  private readonly expanded = signal<ReadonlySet<string>>(new Set());
  // 顏色來自 styles.scss 的 --chart-* token，建構時讀一次
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

  protected readonly hasSpending = computed(() => this.report()?.expenses.some(s => s.total > 0) ?? false);
  protected readonly natureChart = computed(() => {
    const report = this.report();
    return report && this.hasSpending() ? natureDoughnut(report, this.palette) : null;
  });
  protected readonly floatingChart = computed(() => {
    const report = this.report();
    const hasFloating = report?.expenses.some(s => s.kind === 'Floating' && s.lines.some(l => l.amount > 0)) ?? false;
    return report && hasFloating ? floatingDoughnut(report, this.palette) : null;
  });

  protected readonly formatAmount = formatAmount;

  protected changeMonth(month: number): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: { month }, queryParamsHandling: 'merge' });
  }

  protected toggleLine(key: string): void {
    this.expanded.update(expanded => toggle(expanded, key));
  }
}
