import { Component, ElementRef, EventEmitter, OnDestroy, Output, ChangeDetectionStrategy, effect, input, signal, viewChild } from '@angular/core';
import { Chart, Filler, LineController, LineElement, LinearScale, CategoryScale, PointElement, Tooltip } from 'chart.js';
import { AnalyticsBreakdown, AnalyticsMetric } from '../../services/analytics.service';
import { TrafficPoint, buildTrafficChartConfig } from './traffic-chart.config';

export type { TrafficPoint } from './traffic-chart.config';

Chart.register(LineController, LineElement, PointElement, LinearScale, CategoryScale, Filler, Tooltip);

@Component({
  selector: 'ta-traffic-chart',
  templateUrl: './traffic-chart.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TrafficChartComponent implements OnDestroy {
  pageviews = input<TrafficPoint[]>([]);
  visitors = input<TrafficPoint[]>([]);
  events = input<TrafficPoint[]>([]);
  breakdown = input<AnalyticsBreakdown | null>(null);

  @Output() chartClick = new EventEmitter<void>();

  metric = signal<AnalyticsMetric>('pageviews');

  private canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private chart?: Chart;

  get data(): TrafficPoint[] {
    // Automatically show whichever metric has data
    if (this.pageviews().length > 0) return this.pageviews();
    if (this.visitors().length > 0) return this.visitors();
    if (this.events().length > 0) return this.events();
    return [];
  }

  get metricName(): string {
    if (this.pageviews().length > 0) return 'Pageviews';
    if (this.visitors().length > 0) return 'Visitors';
    if (this.events().length > 0) return 'Events';
    return '';
  }

  private render = effect(() => {
    // Read the signals so the effect re-runs when any input changes.
    this.pageviews(); this.visitors(); this.events();
    const breakdown = this.breakdown();
    const canvas = this.canvas().nativeElement;

    this.chart?.destroy();
    this.chart = new Chart(canvas, buildTrafficChartConfig(this.data, this.metricName, breakdown, () => this.chartClick.emit()));
  });

  ngOnDestroy(): void {
    this.chart?.destroy();
  }
}
