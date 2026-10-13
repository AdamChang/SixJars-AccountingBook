import { ChartConfiguration } from 'chart.js';
import { MonthlyReportDto, ReportLineDto, ReportSectionDto, YearlyReportDto } from '../../core/api/dto';
import {
  ChartPalette, floatingDoughnut, formatShare, natureDoughnut, readChartPalette, toggle, visibleLines, yearlyStackedBar,
} from './report-rules';

const line = (kind: ReportLineDto['kind'], id: string, name: string, amount: number, children: ReportLineDto[] = []): ReportLineDto =>
  ({ kind, id, name, amount, principal: null, interest: null, children });
const FOOD = line('Category', 'cat-food', '飲食', 150, [
  line('Category', 'cat-lunch', '午餐', 100),
  line('Unassigned', 'cat-food', '（未分子類）', 50),
]);
const section = (kind: ReportSectionDto['kind'], total: number, lines: ReportLineDto[] = []): ReportSectionDto =>
  ({ kind, total, unpaid: kind === 'Floating' ? null : 0, lines });
const PALETTE: ChartPalette = {
  nature: { Floating: '#1', Fixed: '#2', Loan: '#3', Special: '#4' },
  series: ['#a', '#b'],
};
const monthly = (expenses: ReportSectionDto[]): MonthlyReportDto => ({
  budgetMonth: 202602, monthlyDisposable: 0, income: { kind: 'Income', total: 0, unpaid: null, lines: [] },
  expenses, expenseTotal: 0, unpaidTotal: 0, planning: [],
});

describe('visibleLines', () => {
  it('shows_only_top_level_when_collapsed', () => {
    const rows = visibleLines([FOOD], new Set());
    expect(rows.map(r => [r.line.name, r.depth, r.expandable, r.expanded])).toEqual([['飲食', 0, true, false]]);
  });

  it('shows_children_one_level_deeper_with_unique_keys', () => {
    const rows = visibleLines([FOOD], new Set(['Category:cat-food']));
    expect(rows.map(r => [r.line.name, r.depth])).toEqual([['飲食', 0], ['午餐', 1], ['（未分子類）', 1]]);
    // 主分類與「（未分子類）」的 id 相同，鍵要帶 kind
    expect(new Set(rows.map(r => r.key)).size).toBe(3);
    expect(rows[2].expandable).toBe(false);
  });
});

describe('toggle', () => {
  it('adds_then_removes_without_mutating', () => {
    const empty = new Set<string>();
    const opened = toggle(empty, 'k');
    expect([...opened]).toEqual(['k']);
    expect(empty.size).toBe(0);
    expect([...toggle(opened, 'k')]).toEqual([]);
  });
});

describe('natureDoughnut', () => {
  it('uses_positive_section_totals_in_fixed_order_with_nature_colors', () => {
    const config = natureDoughnut(monthly([
      section('Floating', 500), section('Fixed', 0), section('Loan', 2000), section('Special', -30),
    ]), PALETTE);
    expect(config.type).toBe('doughnut');
    expect(config.data.labels).toEqual(['浮動支出', '貸款支出']);
    expect(config.data.datasets[0].data).toEqual([500, 2000]);
    expect(config.data.datasets[0].backgroundColor).toEqual(['#1', '#3']);
  });
});

describe('floatingDoughnut', () => {
  it('uses_positive_floating_mains_and_cycles_series_colors', () => {
    const config = floatingDoughnut(monthly([
      section('Floating', 230, [FOOD, line('Category', 'cat-fun', '娛樂', 80), line('Category', 'cat-x', '退款', -10), line('Category', 'cat-y', '交通', 10)]),
    ]), PALETTE);
    expect(config.data.labels).toEqual(['飲食', '娛樂', '交通']);
    expect(config.data.datasets[0].data).toEqual([150, 80, 10]);
    expect(config.data.datasets[0].backgroundColor).toEqual(['#a', '#b', '#a']);
  });
});

describe('yearlyStackedBar', () => {
  it('stacks_four_expense_natures_over_twelve_months', () => {
    const figures = (n: number) => ({ months: Array.from({ length: 12 }, (_, i) => (i === 0 ? n : 0)), total: n, shareOfIncome: null, monthlyAverage: null });
    const report: YearlyReportDto = {
      year: 2026, averageDivisor: 12, averageFromMonth: 1, income: { kind: 'Income', figures: figures(9), lines: [] },
      expenses: (['Floating', 'Fixed', 'Loan', 'Special'] as const).map((kind, i) => ({ kind, figures: figures(i + 1), lines: [] })),
      expenseTotal: figures(10), planning: { allocation: [], withdrawal: [], return: [] },
    };
    const config = yearlyStackedBar(report, PALETTE);
    expect(config.type).toBe('bar');
    expect(config.data.labels).toEqual(['1月', '2月', '3月', '4月', '5月', '6月', '7月', '8月', '9月', '10月', '11月', '12月']);
    expect(config.data.datasets.map(d => [d.label, d.data[0], d.backgroundColor])).toEqual([
      ['浮動支出', 1, '#1'], ['固定支出', 2, '#2'], ['貸款支出', 3, '#3'], ['特別支出', 4, '#4'],
    ]);
    // 寬的 ChartConfiguration 的 scales 是所有 scale 型別的聯集，在測試端轉成 bar 才看得到 stacked
    const scales = (config as ChartConfiguration<'bar'>).options?.scales;
    expect(scales?.['x']?.stacked).toBe(true);
    expect(scales?.['y']?.stacked).toBe(true);
  });
});

describe('formatShare', () => {
  it('formats_ratio_as_percent_or_dash', () => {
    expect(formatShare(0.1234)).toBe('12.3%');
    expect(formatShare(1)).toBe('100.0%');
    expect(formatShare(null)).toBe('—');
  });
});

describe('readChartPalette', () => {
  it('reads_css_tokens', () => {
    const tokens: Record<string, string> = {
      '--chart-floating': ' #1 ', '--chart-fixed': '#2', '--chart-loan': '#3', '--chart-special': '#4',
      '--chart-series-1': '#a', '--chart-series-2': '#b',
    };
    const palette = readChartPalette({ getPropertyValue: (name: string) => tokens[name] ?? '' });
    expect(palette).toEqual(PALETTE);
  });
});
