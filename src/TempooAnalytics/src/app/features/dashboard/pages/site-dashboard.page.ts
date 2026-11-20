import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { firstValueFrom, timer } from 'rxjs';
import { CommonModule, NgComponentOutlet, formatDate } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { DatePicker } from 'primeng/datepicker';
import { AnalyticsService, DashboardDto, SiteSummary, AnalyticsBreakdown, FunnelDefinition, AdvancedFilter } from '@app/shared/services/analytics.service';
import { FunnelService } from '@app/shared/services/funnel.service';
import { MetricCardComponent } from '@shared/components/metric-card/metric-card';
import { TrafficChartComponent } from '../../../shared/components/traffic-chart/traffic-chart';
import { TopDevicesComponent } from '@shared/components/analytics/top-devices/top-devices';
import { TopLocationsComponent } from '@app/shared/components/top-locations/top-locations';
import { COUNTRY_NAMES } from '@app/shared/components/location-map/location-map';
import { FunnelBuilderComponent } from '@shared/components/analytics/funnel-builder/funnel-builder.component';
import { HeatmapPanelComponent } from '@shared/components/analytics/heatmap-panel/heatmap-panel.component';
import { SetupGuideModalComponent, EmptyStateComponent } from '@shared/components';
import { HourlyActivityComponent } from '@shared/components/analytics/hourly-activity/hourly-activity.component';
import { ReferrersModalComponent } from '../components/referrers-modal/referrers-modal.component';
import { PagesModalComponent } from '../components/pages-modal/pages-modal.component';
import { EventsModalComponent } from '../components/events-modal/events-modal.component';
import { DASHBOARD_EXTENSIONS } from '@app/core/dashboard-extensions';
import { SiteService } from '@shared/services/site.service';
import { ToastService } from '@app/shared/services/toast.service';
import { FeatureFlagsService } from '@app/shared/services/feature-flags.service';
import { ENTITLEMENTS } from '@app/core/entitlements';
import { UPSELL_COMPONENT } from '@app/core/upsell';
import { AuthService } from '@app/shared/services/auth.service';
import { BreakdownSelectorComponent } from '@shared/components/analytics/breakdown-selector/breakdown-selector';

@Component({
    selector: 'app-site-dashboard',
    standalone: true,
    imports: [
    CommonModule,
    FormsModule,
    DatePicker,
    MetricCardComponent,
    TrafficChartComponent,
    TopLocationsComponent,
    TopDevicesComponent,
    FunnelBuilderComponent,
    HeatmapPanelComponent,
    SetupGuideModalComponent,
    EmptyStateComponent,
    HourlyActivityComponent,
    ReferrersModalComponent,
    PagesModalComponent,
    EventsModalComponent,
    BreakdownSelectorComponent,
    NgComponentOutlet
],
    templateUrl: './site-dashboard.page.html',
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
export class DashboardPage {
    router = inject(Router);
    private route = inject(ActivatedRoute);
    private analytics = inject(AnalyticsService);
    private funnels = inject(FunnelService);
    private siteSvc = inject(SiteService);
    private toast = inject(ToastService);
    private featureFlags = inject(FeatureFlagsService);
    auth = inject(AuthService);

    // 1. Resources (Modern Angular way)
    sitesResource = this.analytics.sitesResource;
    
    // Sticky Data Signals (To avoid flickering during refreshes)
    private _stickySites = signal<SiteSummary[]>([]);
    sites = this._stickySites.asReadonly();

    // Selection & Range State
    selectedSiteKey = signal<string>('');
    dateRange = signal<Date[] | undefined>(undefined);
    selectedRange = signal<number>(30); 
    selectedBreakdown = signal<AnalyticsBreakdown | null>(null);
    isPolling = signal(true);
    refreshTick = signal(0); // Trigger for periodic refresh

    currentBreakdown = computed(() => {
        const selected = this.selectedBreakdown();
        if (selected) return selected;

        // Default logic based on range
        const range = this.dateRange();
        if (range && range[0] && range[1]) {
            const diffMs = range[1].getTime() - range[0].getTime();
            const diffDays = diffMs / (1000 * 60 * 60 * 24);
            
            if (diffDays <= 1.1) return 'hour';
            if (diffDays <= 31) return 'day';
            if (diffDays <= 90) return 'week';
            return 'month';
        }
        
        // Fallback for relative ranges
        const days = this.selectedRange();
        if (days <= 1) return 'hour';
        if (days <= 31) return 'day';
        return 'week';
    });

    // Plan-based feature signals
    private entitlements = inject(ENTITLEMENTS);
    private extensionRegistry = inject(DASHBOARD_EXTENSIONS, { optional: true });
    /** Blocks contributed by feature packages (they receive the selected site as `siteId`). */
    extensions = computed(() => (this.extensionRegistry ?? []).flat()
        .filter(e => !e.visible || e.visible(this.featureFlags))
        .sort((a, b) => (a.order ?? 100) - (b.order ?? 100)));
    planFeatures = this.entitlements.features;
    /** False in builds without plans (community): the Leads tab is a plan feature with no screen of its own. */
    hasPlans = this.entitlements.hasPlans;
    leads = computed(() => this.planFeatures().leads);
    advancedAnalytics = computed(() => this.planFeatures().advancedAnalytics);
    upsell = inject(UPSELL_COMPONENT);
    heatmapsEnabled = this.featureFlags.heatmaps;

    // Sharing & Export signals
    loadingShare = signal(false);
    publicLink = signal<string | null>(null);
    isExporting = signal(false);

    // Custom range signals
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

    maxDateTime = signal<string>(new Date().toISOString().slice(0, 16));
    maxDateObj = new Date();

    private _stickyDashboardData = signal<DashboardDto | undefined>(undefined);

    constructor() {
        const to = new Date();
        const from = new Date();
        from.setDate(to.getDate() - 30);
        this.dateRange.set([from, to]);

        // Listen to query params for site selection
        this.route.queryParams.subscribe(params => {
            const siteKey = params['site'];
            if (siteKey) {
                this.selectedSiteKey.set(siteKey);
            }
        });

        // Background update for max date
        timer(60000, 60000).subscribe(() => {
            this.maxDateTime.set(this.getLocalISOString());
            this.maxDateObj = new Date();
        });

        // Polling tick
        timer(10000, 10000).subscribe(() => {
            if (this.isPolling()) {
                this.refreshTick.update(v => v + 1);
            }
        });

        // Sync Sites
        effect(() => {
            const val = this.sitesResource.value();
            if (val) this._stickySites.set(val);
        });

        // Sync Dashboard Data
        effect(() => {
            const val = this.dashboardResource.value();
            if (val) this._stickyDashboardData.set(val);
        });

        // Export status is set reactively only when the user triggers an export via exportData()

        // Reset data on site change to avoid showing old data
        effect(() => {
            this.selectedSiteKey();
            this._stickyDashboardData.set(undefined);
        });
    }

    private getLocalISOString(date: Date = new Date()): string {
        const offset = date.getTimezoneOffset();
        const localDate = new Date(date.getTime() - (offset * 60 * 1000));
        return localDate.toISOString().slice(0, 16);
    }

    showSetupGuide = signal(false);
    showFilterBar = signal(false);
    activeFilters = signal<{ property: string, operator: string, values: string[], newValue?: string }[]>([]); 

    currentSite = computed(() => {
        const key = this.selectedSiteKey();
        return this.sites().find(s => s.siteKey === key);
    });

    availableProperties = [
        { label: 'Path', value: 'path' },
        { label: 'Page Title', value: 'page title' },
        { label: 'Country', value: 'country' },
        { label: 'Browser', value: 'browser' },
        { label: 'OS', value: 'os' },
        { label: 'Device', value: 'device' },
        { label: 'Referrer', value: 'referrer' },
        { label: 'Event Name', value: 'event name' },
        { label: 'User ID', value: 'user id' }
    ];

    availableOperators = [
        { label: 'Is', value: 'is' },
        { label: 'Is not', value: 'is_not' },
        { label: 'Contains', value: 'contains' },
        { label: 'Not contains', value: 'not_contains' },
        { label: 'Matches regex', value: 'matches' },
        { label: 'Not matches regex', value: 'not_matches' }
    ];

    // Main Dashboard Resource
    dashboardResource = this.analytics.getDashboardResource(() => ({
        days: this.selectedRange(),
        siteKey: this.selectedSiteKey() || undefined,
        from: this.customFrom() || undefined,
        to: this.customTo() || undefined,
        breakdown: this.currentBreakdown(),
        tick: this.refreshTick(), // dependency to trigger refresh
        filter: { 
            advancedFilters: this.activeFilters()
                .filter(f => f.values && f.values.length > 0)
                .map(({ property, operator, values }) => ({ property, operator, values }))
        }
    }));

    // Computed data derived from the resource
    private initialDashboard: DashboardDto = {
        dailyMetrics: [], topSites: [], locations: [], devices: [], browsers: [], os: [],
        referrers: [], channels: [], topPages: [], pageTitles: [], entryPages: [], exitPages: [],
        technicalTraffic: [], botTraffic: [], companies: [], screens: [], customEvents: [], funnels: [], 
        utmSources: [], utmMediums: [], utmCampaigns: [], weeklyTrends: [], totalSites: 0,
        metrics30: { pageviews30: 0, visitors30: 0, sessions30: 0, events30: 0, customEvents30: 0, bounceRate30: 0, pagesPerSession30: 0, conversionRate30: 0, avgSessionDuration30: 0, avgPageDuration30: 0 },
        metrics30Previous: { pageviews30: 0, visitors30: 0, sessions30: 0, events30: 0, customEvents30: 0, bounceRate30: 0, pagesPerSession30: 0, conversionRate30: 0, avgSessionDuration30: 0, avgPageDuration30: 0 }
    };

    dashboardData = computed(() => this._stickyDashboardData() || this.initialDashboard);

    /** Note shown when the selected range touches imported history. */
    importedDataNote = computed<string | null>(() => {
        if (!this.featureFlags.plausibleImport()) return null;
        const data = this.dashboardData();
        if (data.importedDataExcludedByFilter) return 'Imported history can\'t be filtered, so it is not included while filters are applied.';
        if (data.includesImportedData) return 'Includes imported history. Visitors on imported days are added up per day.';
        return null;
    });

    // Reactive data helpers
    pageviewsData = computed(() => this.dashboardData().dailyMetrics.map(m => ({ date: m.date, value: m.pageviews })));
    visitorsData = computed(() => this.dashboardData().dailyMetrics.map(m => ({ date: m.date, value: m.visitors })));
    eventsData = computed(() => this.dashboardData().dailyMetrics.map(m => ({ date: m.date, value: m.events })));
    weeklyTrendsData = computed(() => this.dashboardData().weeklyTrends ?? []);
    devicesData = computed(() => this.dashboardData().devices ?? []);
    browsersData = computed(() => this.dashboardData().browsers ?? []);
    osData = computed(() => this.dashboardData().os ?? []);
    referrersData = computed(() => this.dashboardData().referrers ?? []);
    channelsData = computed(() => this.dashboardData().channels ?? []);
    topPagesData = computed(() => this.dashboardData().topPages ?? []);
    pageTitlesData = computed(() => this.dashboardData().pageTitles ?? []);
    entryPagesData = computed(() => this.dashboardData().entryPages ?? []);
    exitPagesData = computed(() => this.dashboardData().exitPages ?? []);
    technicalTrafficData = computed(() => this.dashboardData().technicalTraffic ?? []);
    botTrafficData = computed(() => this.dashboardData().botTraffic ?? []);
    companiesData = computed(() => this.dashboardData().companies ?? []);
    screensData = computed(() => this.dashboardData().screens ?? []);
    customEventsData = computed(() => this.dashboardData().customEvents ?? []);
    funnelsData = computed(() => this.dashboardData().funnels ?? []);
    metrics = computed(() => this.dashboardData().metrics30);
    previousMetrics = computed(() => this.dashboardData().metrics30Previous);

    referrerTab = signal<'referrers' | 'channels'>('referrers');
    techTab = signal<'devices' | 'browsers' | 'os' | 'screens'>('devices');
    pagesTab = signal<'pages' | 'titles' | 'entry' | 'exit'>('pages');

    // UI Helpers
    getChannelIcon(channel: string): string {
        switch (channel.toLowerCase()) {
            case 'direct': return 'pi pi-directions';
            case 'organic search': return 'pi pi-search';
            case 'organic social': return 'pi pi-share-alt';
            case 'ai tools': return 'pi pi-sparkles';
            case 'referral': return 'pi pi-link';
            default: return 'pi pi-question-circle';
        }
    }

    getTechIcon(type: 'browser' | 'os', value: string): string {
        const val = value.toLowerCase();
        if (type === 'browser') {
            if (val.includes('chrome')) return 'pi pi-google';
            if (val.includes('firefox')) return 'pi pi-github'; 
            if (val.includes('safari')) return 'pi pi-compass';
            if (val.includes('edge')) return 'pi pi-microsoft';
            return 'pi pi-globe';
        } else {
            if (val.includes('windows')) return 'pi pi-microsoft';
            if (val.includes('mac') || val.includes('ios')) return 'pi pi-apple';
            if (val.includes('android')) return 'pi pi-android';
            if (val.includes('linux')) return 'pi pi-server';
            return 'pi pi-desktop';
        }
    }

    locationData = computed(() => this.dashboardData().locations ?? []);
    locationsTable = computed(() => {
        const data = this.locationData();
        const total = data.reduce((acc, curr) => acc + curr.value, 0);
        return data.map(l => ({
            country: COUNTRY_NAMES[l.countryCode] || l.countryCode,
            countryCode: l.countryCode,
            visitors: l.value,
            percent: total > 0 ? parseFloat(((l.value / total) * 100).toFixed(1)) : 0
        }));
    });

    // Auto-selection of first site
    private siteSelectionEffect = effect(() => {
        const sites = this.sites();
        const current = this.selectedSiteKey();
        if (sites.length > 0 && !current) {
            this.selectedSiteKey.set(sites[0].siteKey);
        }
    });

    // Filter Logic
    toggleFilterBar() { this.showFilterBar.update(v => !v); if (this.showFilterBar() && this.activeFilters().length === 0) this.addFilter(); }
    addFilter() { this.activeFilters.update(f => [...f, { property: 'path', operator: 'is', values: [], newValue: '' }]); }
    removeFilter(index: number) { this.activeFilters.update(f => f.filter((_, i) => i !== index)); }
    removeFilterTag(filterIndex: number, tagIndex: number) {
        this.activeFilters.update(filters => {
            const updated = [...filters];
            updated[filterIndex] = { ...updated[filterIndex], values: updated[filterIndex].values.filter((_, i: number) => i !== tagIndex) };
            return updated;
        });
    }
    updateFilterValue(index: number, value: string) {
        if (!value) return;
        this.activeFilters.update(filters => {
            const updated = [...filters];
            if (!updated[index].values.includes(value)) {
                updated[index] = { ...updated[index], values: [...updated[index].values, value], newValue: '' };
            }
            return updated;
        });
    }
    clearAllFilters() { this.activeFilters.set([]); this.showFilterBar.set(false); }
    applyFilters() { this.refreshTick.update(v => v + 1); }
    updateFilterProperty(index: number, prop: string) { this.activeFilters.update(filters => { const updated = [...filters]; updated[index] = { ...updated[index], property: prop }; return updated; }); }
    updateFilterOperator(index: number, op: string) { this.activeFilters.update(filters => { const updated = [...filters]; updated[index] = { ...updated[index], operator: op }; return updated; }); }

    // Modals
    showFunnelModal = signal(false);
    showReferrersModal = signal(false);
    showPagesModal = signal(false);
    showEventsModal = signal(false);
    eventModalFilter = signal<string | undefined>(undefined);
    funnelToEdit = signal<FunnelDefinition | undefined>(undefined);
    deletingFunnel = signal(false);

    openReferrersModal() { this.showReferrersModal.set(true); }
    closeReferrersModal() { this.showReferrersModal.set(false); }
    openPagesModal() { this.showPagesModal.set(true); }
    closePagesModal() { this.showPagesModal.set(false); }
    openEventsModal(eventName?: string) { 
        this.eventModalFilter.set(eventName);
        this.showEventsModal.set(true); 
    }
    closeEventsModal() { 
        this.showEventsModal.set(false);
        this.eventModalFilter.set(undefined);
    }
    openNewFunnelModal() { if (!this.selectedSiteId()) return; this.funnelToEdit.set(undefined); this.showFunnelModal.set(true); }
    editFunnel(funnel: FunnelDefinition) { this.funnelToEdit.set(funnel); this.showFunnelModal.set(true); }
    onFunnelSaved() { this.showFunnelModal.set(false); this.funnelToEdit.set(undefined); this.refreshTick.update(v => v + 1); }
    closeFunnelModal() { this.showFunnelModal.set(false); this.funnelToEdit.set(undefined); }
    async deleteFunnel() {
        const funnel = this.funnelToEdit();
        if (!funnel?.id || !confirm(`Are you sure you want to delete the funnel "${funnel.name}"?`)) return;
        this.deletingFunnel.set(true);
        try {
            await firstValueFrom(this.funnels.deleteFunnel(funnel.id, funnel.siteId || this.selectedSiteId()));
            this.toast.success('Funnel deleted');
            this.onFunnelSaved();
        } catch { this.toast.error('Failed to delete funnel'); } finally { this.deletingFunnel.set(false); }
    }
    selectedSiteId = computed(() => this.sites().find(s => s.siteKey === this.selectedSiteKey())?.siteId || '');

    async onDeleteSite(site: SiteSummary) {
        if (confirm(`Are you sure you want to delete ${site.name}?`)) {
            await firstValueFrom(this.analytics.deleteSite(site.siteKey));
            this.sitesResource.reload();
        }
    }

    async sharePublicLink() {
        const key = this.selectedSiteKey();
        if (!key) return;
        this.loadingShare.set(true);
        try {
            const res = await firstValueFrom(this.siteSvc.shareSite(key));
            const url = `${window.location.origin}/preview/${res.token}`;
            this.publicLink.set(url);
            try { await navigator.clipboard.writeText(url); this.toast.success('Link ready and copied!', 2000); } catch (e) { }
        } catch (error) { this.toast.error('Failed to generate sharing link'); } finally { this.loadingShare.set(false); }
    }

    copyPublicLink() {
        const link = this.publicLink();
        if (!link) return;
        navigator.clipboard.writeText(link).then(() => { this.toast.success('Link copied', 2000); this.publicLink.set(null); });
    }

    exportData() {
        const site = this.currentSite();
        if (!site) return;

        if (this.entitlements.isFree()) {
            this.toast.info(this.entitlements.planHint('export'));
            this.router.navigate(['/account/profile']);
            return;
        }

        const range = this.dateRange();
        let from: string, to: string;

        if (range && range[0] && range[1]) {
            from = range[0].toISOString();
            to = range[1].toISOString();
        } else {
            const end = new Date();
            const start = new Date();
            start.setDate(end.getDate() - (this.selectedRange() || 30));
            from = start.toISOString();
            to = end.toISOString();
        }

        this.analytics.exportSiteData(site.siteId, from, to).subscribe({
            next: () => {
                this.isExporting.set(true);
                this.toast.success('Export started! You will receive an email shortly.');
            },
            error: (err) => {
                this.isExporting.set(false);
                this.toast.error('Export failed: ' + (err.error?.error || 'Unknown error'));
            }
        });
    }

    setRange(days: number) {
        this.selectedRange.set(days);
        const to = new Date();
        const from = new Date();
        from.setDate(to.getDate() - (days === 0 ? 30 : days));
        this.dateRange.set([from, to]);
        this.selectedBreakdown.set(null);
    }

    onDateChange(event: (Date | null)[] | null) {
        this.dateRange.set(event as Date[] | undefined);
        if (event && event[0] && event[1]) {
            this.selectedRange.set(0);
            this.selectedBreakdown.set(null);
        }
    }

    togglePolling() { this.isPolling.update(v => !v); }

    // Helpers
    formatDate(dateStr: string): string { return formatDate(dateStr, 'MMM d, yyyy', 'en-US'); }
    formatNumber(value: number): string { return value.toLocaleString(); }
    formatDuration(seconds: number): string {
        if (!seconds) return '0s';
        const mins = Math.floor(seconds / 60);
        const secs = Math.round(seconds % 60);
        return mins > 0 ? `${mins}m ${secs}s` : `${secs}s`;
    }
    getTrend(current: number, previous: number): { value: number, positive: boolean } {
        if (!previous) return { value: 0, positive: true };
        const diff = ((current - previous) / previous) * 100;
        return { value: Math.abs(Math.round(diff)), positive: diff >= 0 };
    }

    showEmptyState = computed(() => {
        const data = this._stickyDashboardData();
        if (!data) return false; // Keep the previous state while loading
        return data.dailyMetrics.length === 0 && data.topSites.length === 0;
    });
}
