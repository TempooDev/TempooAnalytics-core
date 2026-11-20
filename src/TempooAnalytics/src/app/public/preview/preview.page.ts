import { Component, inject, signal, computed, OnInit, OnDestroy, ChangeDetectionStrategy, effect } from '@angular/core';
import { APP_SETTINGS } from '@app/core/app-settings';
import { CommonModule, formatDate } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { firstValueFrom, timer } from 'rxjs';
import { FormsModule } from '@angular/forms';
import { DatePicker } from 'primeng/datepicker';
import { HourlyActivity, SiteMetricsSummary, SiteLocation, AnalyticsService, PublicMetric, PublicTopPage, PublicResponse } from '@app/shared/services/analytics.service';
import { HourlyActivityComponent } from "@shared/components/analytics/hourly-activity/hourly-activity.component";
import { TrafficChartComponent } from "@app/shared/components/traffic-chart/traffic-chart";
import { PREVIEW_EXTENSIONS } from '@app/core/preview-extensions';
import { FeatureFlagsService } from '@app/shared/services/feature-flags.service';
import { Device, AnalyticsBreakdown } from '@app/shared/services';
import { TopDevicesComponent } from '@shared/components/analytics/top-devices/top-devices';
import { EmptyStateComponent } from '@shared/components';
import { TopLocationsComponent } from '@shared/components/top-locations/top-locations';
import { COUNTRY_NAMES } from '@shared/components/location-map/location-map';
import { MetricCardComponent } from '@shared/components/metric-card/metric-card';
import { TopPagesComponent } from '@shared/components/analytics/top-pages/top-pages';
import { BreakdownSelectorComponent } from '@shared/components/analytics/breakdown-selector/breakdown-selector';
import { FunnelDefinition } from '@app/shared/services/analytics.service';

@Component({
    selector: 'app-preview-page',
    standalone: true,
    imports: [
        CommonModule,
        FormsModule,
        DatePicker,
        TrafficChartComponent,
        HourlyActivityComponent,
        TopDevicesComponent,
        EmptyStateComponent,
        TopLocationsComponent,
        MetricCardComponent,
        TopPagesComponent,
        BreakdownSelectorComponent
    ],
    templateUrl: './preview.page.html',
    styles: [`
        :host ::ng-deep .tempoo-datepicker {
            width: auto;
        }
        :host ::ng-deep .tempoo-datepicker .p-datepicker-input,
        :host ::ng-deep .tempoo-datepicker .p-inputtext {
            background: transparent !important;
            border: none !important;
            color: #e2e8f0 !important;
            font-size: 0.75rem !important;
            padding: 0 !important;
            cursor: pointer;
            box-shadow: none !important;
        }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class PreviewPage implements OnInit, OnDestroy {
    private route = inject(ActivatedRoute);
    settings = inject(APP_SETTINGS);
    private analytics = inject(AnalyticsService);
    private featureFlags = inject(FeatureFlagsService);
    private extensionRegistry = inject(PREVIEW_EXTENSIONS, { optional: true });
    /** Header blocks contributed by feature packages (e.g. the cloud's trial call to action); none in community. */
    extensions = computed(() => (this.extensionRegistry ?? []).flat()
        .filter(e => !e.visible || e.visible(this.featureFlags))
        .sort((a, b) => (a.order ?? 100) - (b.order ?? 100)));

    token = this.route.snapshot.paramMap.get('token');

    loading = signal(true);
    error = signal<string | null>(null);

    siteName = signal('');
    generatedAt = signal('');
    metrics = signal<PublicMetric[]>([]);

    // Range & Breakdown State
    dateRange = signal<Date[] | undefined>(undefined);
    selectedRange = signal<number>(30); 
    selectedBreakdown = signal<AnalyticsBreakdown | null>(null);

    currentBreakdown = computed(() => {
        const selected = this.selectedBreakdown();
        if (selected) return selected;

        const range = this.dateRange();
        if (range && range[0] && range[1]) {
            const diffMs = range[1].getTime() - range[0].getTime();
            const diffDays = diffMs / (1000 * 60 * 60 * 24);
            if (diffDays <= 1.1) return 'hour';
            if (diffDays <= 31) return 'day';
            if (diffDays <= 90) return 'week';
            return 'month';
        }
        const days = this.selectedRange();
        if (days <= 1) return 'hour';
        if (days <= 31) return 'day';
        return 'week';
    });

    customFrom = computed(() => {
        const range = this.dateRange();
        if (range && range[0]) return this.getLocalISOString(range[0]);
        return null;
    });
    
    customTo = computed(() => {
        const range = this.dateRange();
        if (range && range[1]) return this.getLocalISOString(range[1]);
        if (range && range[0]) return this.getLocalISOString(new Date());
        return null;
    });

    maxDateObj = new Date();

    // Summary signals
    summary = signal<SiteMetricsSummary>({ pageviews: 0, visitors: 0, sessions: 0, totalEvents: 0, customEvents: 0, avgSessionDuration: 0, bounceRate: 0 });
    summaryPrevious = signal<SiteMetricsSummary>({ pageviews: 0, visitors: 0, sessions: 0, totalEvents: 0, customEvents: 0, avgSessionDuration: 0, bounceRate: 0 });

    showEmptyState = computed(() => this.summary().pageviews === 0);

    topPages = signal<PublicTopPage[]>([]);
    hourlyActivity = signal<HourlyActivity[]>([]);
    locations = signal<SiteLocation[]>([]);
    funnels = signal<FunnelDefinition[]>([]);
    devices = signal<Device[]>([]);

    // Live polling only if not custom range
    isLive = computed(() => this.selectedRange() !== 0);
    refreshTick = signal(0);

    constructor() {
        const to = new Date();
        const from = new Date();
        from.setDate(to.getDate() - 30);
        this.dateRange.set([from, to]);

        // Background update for max date
        timer(60000, 60000).subscribe(() => {
            this.maxDateObj = new Date();
        });

        // Polling tick
        timer(30000, 30000).subscribe(() => {
            if (this.isLive()) {
                this.refreshTick.update(v => v + 1);
            }
        });

        // Effect for data loading
        effect((onCleanup) => {
            const token = this.token;
            if (!token) {
                this.error.set('No token provided');
                this.loading.set(false);
                return;
            }

            // Dependencies
            const days = this.selectedRange();
            const breakdown = this.currentBreakdown();
            const fromStr = this.customFrom();
            const toStr = this.customTo();
            const tick = this.refreshTick();

            const sub = this.analytics.getPublicPreview(token, days, breakdown || 'day', fromStr, toStr).subscribe({
                next: (res) => {
                    if (this.siteName() !== res.siteName) this.siteName.set(res.siteName);
                    if (this.generatedAt() !== res.generatedAt) this.generatedAt.set(res.generatedAt);
                    this.metrics.set(res.data);
                    this.topPages.set(res.topPages || []);
                    this.hourlyActivity.set(res.hourlyActivity || []);
                    this.devices.set(res.devices || []);
                    this.locations.set(res.locations || []);
                    this.funnels.set(res.funnels || []);
                    if (res.summary) this.summary.set(res.summary);
                    if (res.metrics30Previous) this.summaryPrevious.set(res.metrics30Previous);
                    
                    this.loading.set(false);
                },
                error: (err) => {
                    this.error.set('Failed to load preview. Link may be invalid or expired.');
                    this.loading.set(false);
                }
            });

            onCleanup(() => sub.unsubscribe());
        });
    }

    ngOnInit() {}
    ngOnDestroy() {}

    // Range Handlers
    setRange(days: number) {
        this.selectedRange.set(days);
        
        const to = new Date();
        const from = new Date();
        from.setDate(to.getDate() - (days === 0 ? 30 : days));
        this.dateRange.set([from, to]);
        
        this.selectedBreakdown.set(null);
        this.loading.set(true);
    }

    onDateChange(event: (Date | null)[] | null) {
        this.dateRange.set(event as Date[] | undefined);
        if (event && event[0] && event[1]) {
            this.selectedRange.set(0); 
            this.selectedBreakdown.set(null);
            this.loading.set(true);
        }
    }

    // Dashboard Helpers
    getTrend(current: number, previous: number) {
        if (!previous || previous === 0) return { value: 0, positive: true };
        const diff = current - previous;
        const percent = (diff / previous) * 100;
        return {
            value: Math.abs(Math.round(percent)),
            positive: diff >= 0
        };
    }

    formatDuration(seconds: number): string {
        if (seconds < 60) return `${Math.round(seconds)}s`;
        const mins = Math.floor(seconds / 60);
        const secs = Math.round(seconds % 60);
        return `${mins}m ${secs}s`;
    }

    private getLocalISOString(date: Date = new Date()): string {
        const tzo = -date.getTimezoneOffset();
        const dif = tzo >= 0 ? '+' : '-';
        const pad = (num: number) => (num < 10 ? '0' : '') + num;
        return date.getFullYear() +
            '-' + pad(date.getMonth() + 1) +
            '-' + pad(date.getDate()) +
            'T' + pad(date.getHours()) +
            ':' + pad(date.getMinutes()) +
            ':' + pad(date.getSeconds()) +
            dif + pad(Math.floor(Math.abs(tzo) / 60)) +
            ':' + pad(Math.abs(tzo) % 60);
    }

    // Computed tables
    locationsTable = computed(() => {
        const data = this.locations();
        const total = data.reduce((acc, curr) => acc + curr.value, 0);
        return data.map(l => ({
            country: COUNTRY_NAMES[l.countryCode] || l.countryCode,
            countryCode: l.countryCode,
            visitors: l.value,
            percent: total > 0 ? parseFloat(((l.value / total) * 100).toFixed(1)) : 0
        }));
    });

    pagesTable = computed(() => this.topPages().map(p => {
        return { path: p.url, views: p.views, avg: this.formatDuration(p.avgTimeOnPage || 0), bounce: '0%' };
    }));

    trafficPageviews = computed(() => this.metrics().map(m => ({ date: m.date, value: m.pageviews })));
    trafficVisitors = computed(() => this.metrics().map(m => ({ date: m.date, value: m.visitors })));
    trafficEvents = computed(() => this.metrics().map(m => ({ date: m.date, value: m.events })));
}
