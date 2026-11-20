import { Injectable, inject } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { Observable } from 'rxjs';
import { FunnelDefinition } from './analytics.service';

export interface FunnelStepResult {
    order: number;
    name: string;
    sessions: number;
    dropOffRate: number;
    conversionRate: number;
}

export interface FunnelResult {
    steps: FunnelStepResult[];
    overallConversionRate: number;
}

export interface EventMetadata {
    eventNames: string[];
    urls: string[];
}

@Injectable({
    providedIn: 'root'
})
export class FunnelService {
    private http = inject(HttpClient);
    private apiUrl = '/api/analytics/funnels';

    // 1. Resources
    getEventMetadataResource(request: () => { siteId: string, days?: number }) {
        return httpResource<EventMetadata>(() => {
            const r = request();
            if (!r.siteId) return undefined;
            return `${this.apiUrl}/event-metadata?siteId=${r.siteId}&days=${r.days || 7}`;
        });
    }

    getFunnelsResource(siteId: () => string) {
        return httpResource<FunnelDefinition[]>(() => {
            const id = siteId();
            if (!id) return undefined;
            return `${this.apiUrl}?siteId=${id}`;
        });
    }

    // 2. Methods
    getEventMetadata(siteId: string, days = 7): Observable<EventMetadata> {
        return this.http.get<EventMetadata>(`${this.apiUrl}/event-metadata`, {
            params: { siteId, days }
        });
    }

    getFunnels(siteId: string): Observable<FunnelDefinition[]> {
        return this.http.get<FunnelDefinition[]>(this.apiUrl, {
            params: { siteId }
        });
    }

    createFunnel(funnel: FunnelDefinition): Observable<string> {
        return this.http.post<string>(this.apiUrl, funnel);
    }

    updateFunnel(id: string, funnel: FunnelDefinition): Observable<void> {
        return this.http.put<void>(`${this.apiUrl}/${id}`, funnel);
    }

    deleteFunnel(id: string, siteId: string): Observable<void> {
        return this.http.delete<void>(`${this.apiUrl}/${id}`, {
            params: { siteId }
        });
    }

    calculateFunnel(funnel: FunnelDefinition, days = 30): Observable<FunnelResult> {
        return this.http.post<FunnelResult>(`${this.apiUrl}/calculate`, funnel, {
            params: { days }
        });
    }
}
