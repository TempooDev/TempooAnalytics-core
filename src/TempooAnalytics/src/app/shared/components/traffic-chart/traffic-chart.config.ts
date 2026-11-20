import type { ChartConfiguration } from 'chart.js';
import { AnalyticsBreakdown } from '../../services/analytics.service';

export interface TrafficPoint {
  date: string;
  value: number;
}

const AXIS_COLOR = '#94a3b8';   // slate-400
const GRID_COLOR = '#334155';   // slate-700
const LINE_COLOR = '#3b82f6';   // blue-500

/** Label shown under each tick; mirrors the old datetime axis formatter. */
export function formatTick(date: Date, breakdown: AnalyticsBreakdown | null, data: TrafficPoint[]): string {
  const time = (d: Date) => d.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit', hour12: false });
  const day = (d: Date) => d.toLocaleDateString('en-US', { day: '2-digit', month: 'short' });

  const first = data.length > 1 ? new Date(data[0].date).getTime() : 0;
  const last = data.length > 1 ? new Date(data[data.length - 1].date).getTime() : 0;
  const rangeMs = last - first;

  if (breakdown === 'hour') {
    return rangeMs > 24 * 3600 * 1000 ? `${day(date)} ${time(date)}` : time(date);
  }
  if (breakdown === 'week') return 'Week ' + day(date);
  if (breakdown === 'month') return date.toLocaleDateString('en-US', { month: 'short', year: '2-digit' });

  if (data.length > 0 && rangeMs / 3600000 > 48) return day(date);
  return time(date);
}

/** Tooltip title: "dd MMM, HH:mm" for hourly points, "dd MMM yyyy" for daily, weekly and monthly ones (no time of day). */
export function formatTooltipTitle(date: Date, breakdown: AnalyticsBreakdown | null = null): string {
  if (breakdown === 'day' || breakdown === 'week' || breakdown === 'month') {
    return date.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }
  const d = date.toLocaleDateString('en-GB', { day: '2-digit', month: 'short' });
  const t = date.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', hour12: false });
  return `${d}, ${t}`;
}

export function formatCount(value: number): string {
  return value >= 1000 ? (value / 1000).toFixed(1) + 'k' : value.toFixed(0);
}

export function buildTrafficChartConfig(
  data: TrafficPoint[],
  metricName: string,
  breakdown: AnalyticsBreakdown | null,
  onClick: () => void
): ChartConfiguration<'line'> {
  const dates = data.map(p => new Date(p.date));
  return {
    type: 'line',
    data: {
      labels: dates.map(d => formatTick(d, breakdown, data)),
      datasets: [{
        label: metricName,
        data: data.map(p => p.value),
        borderColor: LINE_COLOR,
        borderWidth: 3,
        tension: 0.4,
        fill: 'origin',
        backgroundColor: ctx => {
          const { chart } = ctx;
          const area = chart.chartArea;
          if (!area) return 'rgba(59,130,246,0.25)';
          const g = chart.ctx.createLinearGradient(0, area.top, 0, area.bottom);
          g.addColorStop(0, 'rgba(59,130,246,0.45)');
          g.addColorStop(1, 'rgba(59,130,246,0.05)');
          return g;
        },
        pointRadius: 0,
        pointHoverRadius: 4,
        pointHoverBackgroundColor: LINE_COLOR,
      }]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      animation: { duration: 800 },
      interaction: { mode: 'index', intersect: false },
      font: { family: 'Inter, sans-serif' },
      onClick: () => onClick(),
      layout: { padding: { left: 10 } },
      plugins: {
        legend: { display: false },
        tooltip: {
          backgroundColor: '#0f172a',
          borderColor: GRID_COLOR,
          borderWidth: 1,
          bodyFont: { size: 12, family: 'Inter, sans-serif' },
          titleFont: { size: 12, family: 'Inter, sans-serif' },
          callbacks: {
            title: items => items.length ? formatTooltipTitle(dates[items[0].dataIndex], breakdown) : '',
            label: item => `${item.dataset.label}: ${(item.parsed.y ?? 0).toLocaleString()}`
          }
        }
      },
      scales: {
        x: {
          grid: { display: false },
          border: { display: false },
          ticks: { color: AXIS_COLOR, font: { size: 11 }, maxRotation: 0, autoSkip: true, maxTicksLimit: 8 }
        },
        y: {
          beginAtZero: true,
          grid: { color: GRID_COLOR },
          border: { display: false, dash: [4, 4] },
          ticks: { color: AXIS_COLOR, font: { size: 11 }, callback: v => formatCount(Number(v)) }
        }
      }
    }
  };
}
