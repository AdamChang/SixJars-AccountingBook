import { TestBed } from '@angular/core/testing';
import { ChartConfiguration } from 'chart.js';
import { CHART_FACTORY, ChartComponent, ChartFactory } from './chart';

const DOUGHNUT: ChartConfiguration = { type: 'doughnut', data: { labels: ['a'], datasets: [{ data: [1] }] } };
const BAR: ChartConfiguration = { type: 'bar', data: { labels: ['a'], datasets: [{ data: [2] }] } };

interface FakeChart { canvas: HTMLCanvasElement; config: ChartConfiguration; destroy: ReturnType<typeof vi.fn> }

function setup() {
  const created: FakeChart[] = [];
  const factory: ChartFactory = (canvas, config) => {
    const chart = { canvas, config, destroy: vi.fn() };
    created.push(chart);
    return chart;
  };
  TestBed.configureTestingModule({ providers: [{ provide: CHART_FACTORY, useValue: factory }] });
  const fixture = TestBed.createComponent(ChartComponent);
  fixture.componentRef.setInput('config', DOUGHNUT);
  fixture.componentRef.setInput('label', '支出依性質');
  return { fixture, created, canvas: () => (fixture.nativeElement as HTMLElement).querySelector('canvas')! };
}

describe('ChartComponent', () => {
  it('creates_chart_on_its_canvas', async () => {
    const { fixture, created, canvas } = setup();
    await fixture.whenStable();
    expect(created).toHaveLength(1);
    expect(created[0].canvas).toBe(canvas());
    expect(created[0].config).toBe(DOUGHNUT);
    expect(canvas().getAttribute('role')).toBe('img');
    expect(canvas().getAttribute('aria-label')).toBe('支出依性質');
  });

  it('recreates_chart_when_config_changes', async () => {
    const { fixture, created } = setup();
    await fixture.whenStable();
    fixture.componentRef.setInput('config', BAR);
    await fixture.whenStable();
    expect(created).toHaveLength(2);
    expect(created[0].destroy).toHaveBeenCalledTimes(1);
    expect(created[1].config).toBe(BAR);
    expect(created[1].destroy).not.toHaveBeenCalled();
  });

  it('destroys_chart_with_component', async () => {
    const { fixture, created } = setup();
    await fixture.whenStable();
    fixture.destroy();
    expect(created[0].destroy).toHaveBeenCalledTimes(1);
  });
});
