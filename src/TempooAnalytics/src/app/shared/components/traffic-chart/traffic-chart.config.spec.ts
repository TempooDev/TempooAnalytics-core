import { buildTrafficChartConfig, formatCount, formatTick, formatTooltipTitle } from './traffic-chart.config';

const hourly = Array.from({ length: 6 }, (_, i) => ({ date: new Date(2026, 10, 1, i, 0).toISOString(), value: (i + 1) * 500 }));
const daily = Array.from({ length: 5 }, (_, i) => ({ date: new Date(2026, 10, 1 + i * 2).toISOString(), value: i }));

describe('traffic chart config', () => {
  it('formats counts like the old axis (1.2k above 1000)', () => {
    expect(formatCount(999)).toBe('999');
    expect(formatCount(1200)).toBe('1.2k');
  });

  it('shows hours for a single day and day+hour across days with hourly breakdown', () => {
    expect(formatTick(new Date(2026, 10, 1, 9, 5), 'hour', hourly)).toBe('09:05');
    const multi = [{ date: new Date(2026, 10, 1).toISOString(), value: 1 }, { date: new Date(2026, 10, 3).toISOString(), value: 1 }];
    expect(formatTick(new Date(2026, 10, 2, 9, 5), 'hour', multi)).toBe('Nov 02 09:05');
  });

  it('labels weeks and months', () => {
    expect(formatTick(new Date(2026, 10, 2), 'week', daily)).toBe('Week Nov 02');
    expect(formatTick(new Date(2026, 10, 2), 'month', daily)).toBe('Nov 26');
  });

  it('uses days for ranges over 48 hours and times otherwise', () => {
    expect(formatTick(new Date(2026, 10, 3), 'day', daily)).toBe('Nov 03');
    expect(formatTick(new Date(2026, 10, 1, 7, 0), 'day', hourly)).toBe('07:00');
  });

  it('formats the tooltip title as "dd MMM, HH:mm"', () => {
    expect(formatTooltipTitle(new Date(2026, 10, 1, 14, 30))).toBe('01 Nov, 14:30');
    expect(formatTooltipTitle(new Date(2026, 10, 1, 14, 30), 'hour')).toBe('01 Nov, 14:30');
    expect(formatTooltipTitle(new Date(2026, 10, 1, 0, 0), 'day')).toBe('01 Nov 2026');
    expect(formatTooltipTitle(new Date(2026, 10, 1, 2, 0), 'month')).toBe('01 Nov 2026');
  });

  it('builds an area line config with the series values and emits clicks', () => {
    const onClick = vi.fn();
    const config = buildTrafficChartConfig(hourly, 'Pageviews', 'hour', onClick);
    expect(config.type).toBe('line');
    expect(config.data.datasets[0].data).toEqual([500, 1000, 1500, 2000, 2500, 3000]);
    expect(config.data.datasets[0].label).toBe('Pageviews');
    expect(config.data.datasets[0].fill).toBe('origin');
    (config.options!.onClick as () => void)();
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it('builds an empty chart without data', () => {
    const config = buildTrafficChartConfig([], '', null, () => {});
    expect(config.data.labels).toEqual([]);
  });
});
