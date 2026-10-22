import { ChartConfiguration } from 'chart.js';
import { MonthlyReportDto, ReportLineKind, ReportSectionKind, YearlyReportDto } from '../../core/api/dto';

export type ExpenseNatureKind = Exclude<ReportSectionKind, 'Income'>;

export const SECTION_LABELS: Record<ReportSectionKind, string> = {
  Income: '收入', Floating: '浮動支出', Fixed: '固定支出', Loan: '貸款支出', Special: '特別支出',
};
const MONTH_LABELS = Array.from({ length: 12 }, (_, i) => `${i + 1}月`);

interface TreeLine<T> { kind: ReportLineKind; id: string; children: T[] }

export interface VisibleLine<T> { key: string; depth: number; line: T; expandable: boolean; expanded: boolean }

// 主分類與它的「（未分子類）」列 id 相同，鍵要帶 kind
export function lineKey(line: { kind: ReportLineKind; id: string }): string {
  return `${line.kind}:${line.id}`;
}

export function visibleLines<T extends TreeLine<T>>(lines: T[], expanded: ReadonlySet<string>, depth = 0): VisibleLine<T>[] {
  return lines.flatMap(line => {
    const key = lineKey(line);
    const isOpen = expanded.has(key);
    const self: VisibleLine<T> = { key, depth, line, expandable: line.children.length > 0, expanded: isOpen };
    return isOpen ? [self, ...visibleLines(line.children, expanded, depth + 1)] : [self];
  });
}

export function toggle(expanded: ReadonlySet<string>, key: string): Set<string> {
  const next = new Set(expanded);
  if (!next.delete(key)) {
    next.add(key);
  }
  return next;
}

export interface ChartPalette { nature: Record<ExpenseNatureKind, string>; series: string[] }

// 顏色定義在 styles.scss 的 :root（--chart-*），這裡只讀出來交給 Chart.js
export function readChartPalette(style: Pick<CSSStyleDeclaration, 'getPropertyValue'>): ChartPalette {
  const token = (name: string) => style.getPropertyValue(name).trim();
  const series: string[] = [];
  for (let i = 1; token(`--chart-series-${i}`) !== ''; i++) {
    series.push(token(`--chart-series-${i}`));
  }
  return {
    nature: { Floating: token('--chart-floating'), Fixed: token('--chart-fixed'), Loan: token('--chart-loan'), Special: token('--chart-special') },
    series,
  };
}

// 甜甜圈畫不了負數（淨退款），0 也沒有意義：只取正數
export function natureDoughnut(report: MonthlyReportDto, palette: ChartPalette): ChartConfiguration {
  const sections = report.expenses.filter(s => s.total > 0);
  return doughnut(
    sections.map(s => SECTION_LABELS[s.kind]),
    sections.map(s => s.total),
    sections.map(s => palette.nature[s.kind as ExpenseNatureKind]),
  );
}

export function floatingDoughnut(report: MonthlyReportDto, palette: ChartPalette): ChartConfiguration {
  const lines = (report.expenses.find(s => s.kind === 'Floating')?.lines ?? []).filter(l => l.amount > 0);
  return doughnut(
    lines.map(l => l.name),
    lines.map(l => l.amount),
    lines.map((_, i) => palette.series[i % palette.series.length]),
  );
}

export function yearlyStackedBar(report: YearlyReportDto, palette: ChartPalette): ChartConfiguration {
  return {
    type: 'bar',
    data: {
      labels: MONTH_LABELS,
      datasets: report.expenses.map(s => ({
        label: SECTION_LABELS[s.kind],
        data: [...s.figures.months],
        backgroundColor: palette.nature[s.kind as ExpenseNatureKind],
      })),
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      scales: { x: { stacked: true }, y: { stacked: true, beginAtZero: true } },
      plugins: { legend: { position: 'bottom' } },
    },
  };
}

export function formatShare(share: number | null): string {
  return share === null ? '—' : `${(share * 100).toFixed(1)}%`;
}

function doughnut(labels: string[], data: number[], colors: string[]): ChartConfiguration {
  return {
    type: 'doughnut',
    data: { labels, datasets: [{ data, backgroundColor: colors }] },
    options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { position: 'bottom' } } },
  };
}
