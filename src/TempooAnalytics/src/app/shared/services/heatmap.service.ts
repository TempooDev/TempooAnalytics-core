import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export type HeatmapDevice = 'desktop' | 'tablet' | 'mobile';

export interface HeatmapPage {
    path: string;
    clicks: number;
    rageClicks: number;
    deadClicks: number;
}

export interface HeatmapPoint {
    x: number;
    y: number;
    count: number;
}

export interface HeatmapElement {
    selector: string;
    clicks: number;
    rageClicks: number;
    deadClicks: number;
}

export interface ScrollDepth {
    depth: number;
    reachedPct: number;
}

export interface Heatmap {
    path: string;
    device: HeatmapDevice;
    totalClicks: number;
    points: HeatmapPoint[];
    topElements: HeatmapElement[];
    scroll: ScrollDepth[];
}

export interface HeatmapOverlayToken {
    token: string;
    expiresAt: string;
    siteUrl: string | null;
}

export interface HeatmapQuery {
    siteId: string;
    device: HeatmapDevice;
    days: number;
}

@Injectable({
    providedIn: 'root'
})
export class HeatmapService {
    private http = inject(HttpClient);
    private apiUrl = '/api/analytics/heatmaps';

    getPages(query: HeatmapQuery): Observable<HeatmapPage[]> {
        return this.http.get<HeatmapPage[]>(`${this.apiUrl}/pages`, { params: this.params(query) });
    }

    getHeatmap(query: HeatmapQuery, path: string): Observable<Heatmap> {
        return this.http.get<Heatmap>(this.apiUrl, { params: { ...this.params(query), path } });
    }

    createOverlayToken(siteId: string, path: string): Observable<HeatmapOverlayToken> {
        return this.http.post<HeatmapOverlayToken>(`${this.apiUrl}/overlay-token`, { siteId, path });
    }

    /** The customer's page with the token in the hash, where analytics.js loads the overlay. */
    overlayUrl(siteUrl: string, path: string, token: string): string {
        return `${siteUrl.replace(/\/+$/, '')}${path}#tempoo-heatmap=${encodeURIComponent(token)}`;
    }

    // Whole UTC days, today included, like the API's default range.
    private params(query: HeatmapQuery) {
        const end = new Date();
        end.setUTCHours(0, 0, 0, 0);
        end.setUTCDate(end.getUTCDate() + 1);
        const start = new Date(end);
        start.setUTCDate(start.getUTCDate() - query.days);
        return { siteId: query.siteId, device: query.device, from: start.toISOString(), to: end.toISOString() };
    }
}
