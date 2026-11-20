import { inject, Injectable, Signal, computed } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { Observable } from 'rxjs';
import { toSignal } from '@angular/core/rxjs-interop';
import { AuthService } from './auth.service';

export interface SiteSummary {
    siteId: string;
    siteKey: string;
    name: string;
    totalEvents: number;
    pageviews: number;
    visitors: number;
    lastEventAt: string | null;
    writeKey?: string;
    bounceRate?: number;
    allowedOrigins?: string[];
    publicShareToken?: string;
}

export interface DailyMetric {
    date: string; // ISO or yyyy-mm-dd
    pageviews: number;
    visitors: number;
    customEvents: number;
    avgSessionDuration?: number;
}

export interface CustomEventMetric {
    date: string;
    eventName: string;
    data: any;
    siteId: string;
    visitorId: string;
    sessionId?: string;
    url?: string;
}

export interface CustomEventGroupResult {
    keys: { [key: string]: string };
    count: number;
}

export interface DashboardDailyMetric {
    date: string;
    pageviews: number;
    visitors: number;
    events: number;
}

export interface AnalyticsFilterDto {
    deviceType?: string;
    os?: string;
    browser?: string;
    countryCode?: string;
    path?: string;
    referrerDomain?: string;
    utmSource?: string;
    utmMedium?: string;
    utmCampaign?: string;
    advancedFilters?: AdvancedFilter[];
}

export interface DashboardTopSite {
    siteId: string;
    siteKey: string;
    name: string;
    pageviews: number;
    visitors: number;
    events: number;
}

export interface AdvancedFilter {
    property: string;
    operator: string;
    values: string[];
    newValue?: string;
}

export interface DashboardLocation {
    countryCode: string;
    value: number;
}

export interface DashboardMetricCard {
    pageviews30: number;
    visitors30: number;
    sessions30: number;
    events30: number;
    customEvents30: number;
    bounceRate30: number;
    pagesPerSession30: number;
    conversionRate30: number;
    avgSessionDuration30: number;
    avgPageDuration30: number;
}

export interface TechnicalTraffic {
    type: string;
    count: number;
}

export interface BotTraffic {
    name: string;
    count: number;
}

export interface CompanyTraffic {
    name: string;
    domain: string;
    count: number;
}

export interface SiteLocation {
    countryCode: string;
    value: number;
}

export interface Device {
    deviceType: string;
    value: number;
}

export interface TopPage {
    url: string;
    views: number;
    avgTimeOnPage?: number;
}

export interface DashboardDto {
    dailyMetrics: DashboardDailyMetric[];
    topSites: DashboardTopSite[];
    locations: DashboardLocation[];
    devices: Device[];
    browsers: any[];
    os: any[];
    referrers: any[];
    channels: Channel[];
    topPages: any[];
    pageTitles: any[];
    entryPages: any[];
    exitPages: any[];
    screens: any[];
    customEvents: EventSummary[];
    funnels: FunnelDefinition[];
    utmSources: any[];
    utmMediums: any[];
    utmCampaigns: any[];
    weeklyTrends: HourlyActivity[];
    technicalTraffic: TechnicalTraffic[];
    botTraffic: BotTraffic[];
    companies: CompanyTraffic[];
    metrics30: DashboardMetricCard;
    metrics30Previous: DashboardMetricCard;
    totalSites: number;
    /** The range includes imported history (days before the site's first live event). */
    includesImportedData?: boolean;
    /** The range has imported history, but filters left it out. */
    importedDataExcludedByFilter?: boolean;
}

export interface FunnelDefinition {
    id?: string;
    siteId: string;
    name: string;
    steps: FunnelStep[];
    // Extended properties for dashboard
    conversionRate?: number;
    totalSessions?: number;
}

export type FunnelStepType = 'Pageview' | 'CustomEvent';

export interface FunnelStep {
    order: number;
    type: FunnelStepType;
    value: string;
    name?: string;
}

export interface EventSummary {
    eventName: string;
    count: number;
}

export interface SiteMetricsSummary {
    pageviews: number;
    visitors: number;
    sessions: number;
    totalEvents: number;
    customEvents: number;
    avgSessionDuration: number;
    bounceRate: number;
}

export interface ReferrerDetail {
    referrer: string;
    sessions: number;
    percentageSessions: number;
    pageviews: number;
    percentagePageviews: number;
    bounceRate: number;
}

export interface Channel {
    channel: string;
    value: number;
}

export interface PageDetail {
    page: string;
    views: number;
    percentageViews: number;
    visitors: number;
    avgTimeOnPage: number;
    bounceRate: number;
}

export interface HourlyActivity {
    dayOfWeek: number;
    hour: number;
    count: number;
}

export interface EventDetailDto {
    timestamp: string;
    eventName: string;
    trafficType: string;
    countryCode: string;
    cityName: string;
    deviceType: string;
    os: string;
    browser: string;
    url: string;
    data: string;
    sessionId: string;
    visitorId: string;
    userAlias: string;
}

export type AnalyticsBreakdown = 'hour' | 'day' | 'week' | 'month';
export type AnalyticsMetric = 'pageviews' | 'visitors' | 'events';

export interface ExportStatusDto {
    isRunning: boolean;
    job?: {
        id: string;
        status: string;
        createdAt: string;
    };
}

export interface PublicMetric {
    date: string;
    visitors: number;
    pageviews: number;
    events: number;
    avgSessionDuration?: number;
}

export interface PublicTopPage {
    url: string;
    views: number;
    avgTimeOnPage?: number;
}

export interface PublicResponse {
    siteName: string;
    generatedAt: string;
    data: PublicMetric[];
    topPages: PublicTopPage[];
    hourlyActivity?: HourlyActivity[];
    devices?: Device[];
    summary: SiteMetricsSummary;
    metrics30Previous?: SiteMetricsSummary;
    locations?: SiteLocation[];
    funnels?: FunnelDefinition[];
}

@Injectable({ providedIn: 'root' })
export class AnalyticsService {
    private readonly http = inject(HttpClient);
    private readonly auth = inject(AuthService);
    private api = '/api';

    // 1. Sites Summary Resource - Guarded by token
    sitesResource = httpResource<SiteSummary[]>(() => {
        if (!this.auth.token) return undefined;
        return `${this.api}/analytics/sites`;
    });

    // 2. Dashboard Resource Factory
    getDashboardResource(request: () => { 
        days: number, 
        top?: number, 
        siteKey?: string, 
        filter?: AnalyticsFilterDto, 
        from?: string, 
        to?: string,
        breakdown?: AnalyticsBreakdown | null
    }) {
        return httpResource<DashboardDto>(() => {
            const r = request();
            let url = `${this.api}/dashboard?days=${r.days}&top=${r.top || 5}`;
            if (r.siteKey) url += `&siteKey=${r.siteKey}`;
            if (r.from) url += `&from=${encodeURIComponent(r.from)}`;
            if (r.to) url += `&to=${encodeURIComponent(r.to)}`;
            if (r.breakdown) url += `&breakdown=${r.breakdown}`;
            if (r.filter?.advancedFilters?.length) {
                url += `&filters=${encodeURIComponent(JSON.stringify(r.filter.advancedFilters))}`;
            }
            return url;
        });
    }

    // --- Legacy Observable methods (kept for one-off actions or specific needs) ---

    getSitesSummary(): Observable<SiteSummary[]> {
        return this.http.get<SiteSummary[]>(`${this.api}/analytics/sites`);
    }

    getDailyMetrics(siteKey: string, days: number = 30): Observable<DailyMetric[]> {
        return this.http.get<DailyMetric[]>(`${this.api}/analytics/sites/${siteKey}/daily?days=${days}`);
    }

    getCustomEvents(siteKey: string, days: number = 30): Observable<CustomEventMetric[]> {
        return this.http.get<CustomEventMetric[]>(`${this.api}/analytics/sites/${siteKey}/daily/customEvents?days=${days}`);
    }

    getDashboard(days: number = 30, top: number = 5, siteKey?: string, filter?: AnalyticsFilterDto, from?: string, to?: string, breakdown?: AnalyticsBreakdown): Observable<DashboardDto> {
        let url = `${this.api}/dashboard?days=${days}&top=${top}`;
        if (siteKey) url += `&siteKey=${siteKey}`;
        if (from) url += `&from=${encodeURIComponent(from)}`;
        if (to) url += `&to=${encodeURIComponent(to)}`;
        if (breakdown) url += `&breakdown=${breakdown}`;
        if (filter?.advancedFilters?.length) {
            url += `&filters=${encodeURIComponent(JSON.stringify(filter.advancedFilters))}`;
        }
        return this.http.get<DashboardDto>(url);
    }

    getReferrerDetails(days: number = 30, siteKey?: string, filter?: AnalyticsFilterDto, from?: string, to?: string): Observable<ReferrerDetail[]> {
        let url = `${this.api}/dashboard/referrers?days=${days}`;
        if (siteKey) url += `&siteKey=${siteKey}`;
        if (from) url += `&from=${encodeURIComponent(from)}`;
        if (to) url += `&to=${encodeURIComponent(to)}`;
        if (filter?.advancedFilters?.length) {
            url += `&filters=${encodeURIComponent(JSON.stringify(filter.advancedFilters))}`;
        }
        return this.http.get<ReferrerDetail[]>(url);
    }

    getChannelDetails(days: number = 30, siteKey?: string, filter?: AnalyticsFilterDto, from?: string, to?: string): Observable<ReferrerDetail[]> {
        let url = `${this.api}/dashboard/channels?days=${days}`;
        if (siteKey) url += `&siteKey=${siteKey}`;
        if (from) url += `&from=${encodeURIComponent(from)}`;
        if (to) url += `&to=${encodeURIComponent(to)}`;
        if (filter?.advancedFilters?.length) {
            url += `&filters=${encodeURIComponent(JSON.stringify(filter.advancedFilters))}`;
        }
        return this.http.get<ReferrerDetail[]>(url);
    }

    getPageDetails(type: string, days: number = 30, siteKey?: string, filter?: AnalyticsFilterDto, from?: string, to?: string): Observable<PageDetail[]> {
        let url = `${this.api}/dashboard/pages?type=${type}&days=${days}`;
        if (siteKey) url += `&siteKey=${siteKey}`;
        if (from) url += `&from=${encodeURIComponent(from)}`;
        if (to) url += `&to=${encodeURIComponent(to)}`;
        if (filter?.advancedFilters?.length) {
            url += `&filters=${encodeURIComponent(JSON.stringify(filter.advancedFilters))}`;
        }
        return this.http.get<PageDetail[]>(url);
    }

    getEventsDetails(days: number = 30, siteKey?: string, filter?: AnalyticsFilterDto, from?: string, to?: string, limit: number = 100, offset: number = 0): Observable<EventDetailDto[]> {
        let url = `${this.api}/dashboard/events?days=${days}&limit=${limit}&offset=${offset}`;
        if (siteKey) url += `&siteKey=${siteKey}`;
        if (from) url += `&from=${encodeURIComponent(from)}`;
        if (to) url += `&to=${encodeURIComponent(to)}`;
        if (filter?.advancedFilters?.length) {
            url += `&filters=${encodeURIComponent(JSON.stringify(filter.advancedFilters))}`;
        }
        return this.http.get<EventDetailDto[]>(url);
    }

    getGroupedCustomEvents(
        siteKey: string,
        eventName: string,
        keys: string[],
        days: number = 7,
        filters?: Record<string, string>
    ): Observable<CustomEventGroupResult[]> {
        const params = new URLSearchParams();
        params.set('eventName', eventName);
        params.set('keys', keys.join(','));
        if (days) params.set('days', String(days));
        Object.entries(filters || {}).forEach(([k, v]) => {
            if (k && v) params.set(`filter_${k}`, v);
        });
        const url = `${this.api}/analytics/sites/${siteKey}/custom-events/groups?${params.toString()}`;
        return this.http.get<CustomEventGroupResult[]>(url);
    }

    getHourlyActivity(siteKey: string, days: number = 30): Observable<HourlyActivity[]> {
        return this.http.get<HourlyActivity[]>(`${this.api}/analytics/sites/${encodeURIComponent(siteKey)}/activity/hourly?days=${days}`);
    }

    getSiteMetricsSummary(siteKey: string, days: number = 30): Observable<SiteMetricsSummary> {
        return this.http.get<SiteMetricsSummary>(`${this.api}/analytics/sites/${encodeURIComponent(siteKey)}/summary?days=${days}`);
    }

    deleteSite(siteKey: string): Observable<void> {
        return this.http.delete<void>(`${this.api}/admin/sites/${encodeURIComponent(siteKey)}`);
    }

    exportSiteData(siteId: string, from: string, to: string): Observable<{ message: string }> {
        return this.http.post<{ message: string }>(`${this.api}/sites/${siteId}/export`, { from, to });
    }

    getExportStatus(siteId: string): Observable<ExportStatusDto> {
        return this.http.get<ExportStatusDto>(`${this.api}/sites/${siteId}/export/status`);
    }

    getPublicPreview(token: string, days: number, breakdown: string, from?: string | null, to?: string | null): Observable<PublicResponse> {
        let url = `${this.api}/sites/public/preview/${token}?days=${days}&breakdown=${breakdown}`;
        if (from) url += `&from=${encodeURIComponent(from)}`;
        if (to) url += `&to=${encodeURIComponent(to)}`;
        return this.http.get<PublicResponse>(url);
    }
}
