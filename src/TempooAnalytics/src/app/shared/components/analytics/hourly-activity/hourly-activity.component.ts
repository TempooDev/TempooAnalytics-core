import { Component, computed, input, signal, inject, resource } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AnalyticsService, HourlyActivity } from '@shared/services/analytics.service';
import { firstValueFrom } from 'rxjs';

@Component({
    selector: 'app-hourly-activity',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './hourly-activity.component.html',
    styles: [`
    :host { display: block; }
  `]
})
export class HourlyActivityComponent {
    siteKey = input<string | undefined>();
    days = input<number>(30);
    customData = input<HourlyActivity[] | null>(null);

    private analyticsService = inject(AnalyticsService);

    // Resource for loading data
    activityResource = resource({
        params: () => ({ siteKey: this.siteKey(), days: this.days(), custom: this.customData() }),
        loader: async ({ params }) => {
            if (params.custom) return params.custom;
            if (!params.siteKey) return [];
            return firstValueFrom(this.analyticsService.getHourlyActivity(params.siteKey, params.days));
        }
    });

    data = computed(() => this.activityResource.value() || []);
    loading = computed(() => this.activityResource.isLoading());

    // Days Mon-Sun (ClickHouse toDayOfWeek: 1=Mon, ..., 7=Sun)
    daysOfWeek = [1, 2, 3, 4, 5, 6, 7];
    dayLabels = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

    // Hours 0-23
    hours = Array.from({ length: 24 }, (_, i) => i);

    // Helper to get value for a specific day and hour
    getValue(day: number, hour: number): number {
        const item = this.data().find(d => d.dayOfWeek === day && d.hour === hour);
        return item ? Number(item.count) : 0;
    }

    maxCount = computed(() => {
        const counts = this.data().map(d => Number(d.count));
        return Math.max(...counts, 1);
    });

    getColor(day: number, hour: number): string {
        const count = this.getValue(day, hour);
        if (count === 0) return 'transparent';
        
        const max = this.maxCount();
        const intensity = count / max;

        const alpha = 0.1 + (intensity * 0.9);
        return `rgba(14, 165, 233, ${alpha})`;
    }

    formatHour(hour: number) {
        const h = hour % 12 || 12;
        const ampm = hour >= 12 ? 'pm' : 'am';
        return `${h}${ampm}`;
    }

    getTooltip(day: number, hour: number) {
        const idx = this.daysOfWeek.indexOf(day);
        const dayLabel = idx !== -1 ? this.dayLabels[idx] : '';
        return `${dayLabel} ${this.formatHour(hour)}: ${this.getValue(day, hour)} events`;
    }
}
