import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';

export enum ImportType {
  GoogleAnalytics4 = 0,
  CSV = 1,
  Plausible = 2
}

export enum ImportStatus {
  Pending = 0,
  Processing = 1,
  Completed = 2,
  Failed = 3,
  Cancelled = 4
}

export interface ImportJobDto {
  id: string;
  siteId: string;
  type: ImportType;
  status: ImportStatus;
  createdAt: string;
  rowsProcessed: number;
  errorMessage?: string;
  startedAt?: string | null;
  completedAt?: string | null;
}

export interface Ga4Property {
  /** "properties/123" */
  id: string;
  name: string;
  account: string;
}

export interface Ga4Connection {
  /** Empty when the Google account can't read any GA4 property. */
  connectionId: string;
  properties: Ga4Property[];
}

/** Site whose import started the Google authorization; read back on the callback route. */
export const GA4_SITE_STORAGE_KEY = 'tempoo.ga4ImportSiteId';

@Injectable({
  providedIn: 'root'
})
export class ImportService {
  private readonly http = inject(HttpClient);
  private readonly api = '/api';

  getImports(siteId: string): Observable<ImportJobDto[]> {
    return this.http.get<ImportJobDto[]>(`${this.api}/sites/${siteId}/imports`);
  }

  /** Plausible export .zip (or a single daily-totals .csv). */
  uploadPlausibleExport(siteId: string, file: File): Observable<{ jobId: string }> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<{ jobId: string }>(`${this.api}/sites/${siteId}/imports/plausible`, formData);
  }

  /** Google consent URL for this site (read-only access to Google Analytics). */
  authorizeGa4(siteId: string): Observable<{ authorizationUrl: string }> {
    return this.http.post<{ authorizationUrl: string }>(`${this.api}/sites/${siteId}/imports/ga4/authorize`, {});
  }

  /** Hands Google's authorization code to the API, which lists the GA4 properties the account can read. */
  connectGa4(siteId: string, code: string, state: string): Observable<Ga4Connection> {
    return this.http.post<Ga4Connection>(`${this.api}/sites/${siteId}/imports/ga4/connect`, { code, state });
  }

  startGa4Import(siteId: string, connectionId: string, propertyId: string): Observable<{ jobId: string }> {
    return this.http.post<{ jobId: string }>(`${this.api}/sites/${siteId}/imports/ga4`, { connectionId, propertyId });
  }

  hasActiveImport(siteId: string): Observable<boolean> {
    return this.http.get<ImportJobDto[]>(`${this.api}/sites/${siteId}/imports`).pipe(
      map(jobs => jobs.some(j => j.status === ImportStatus.Pending || j.status === ImportStatus.Processing))
    );
  }
}

