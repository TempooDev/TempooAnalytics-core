import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface WriteKeyResult {
    siteKey: string;
    writeKey: string;
}

@Injectable({ providedIn: 'root' })
export class SiteService {
    private readonly http = inject(HttpClient);
    private api = '/api';

    generateWriteKey(siteKey: string): Observable<WriteKeyResult> {
        return this.http.post<WriteKeyResult>(`${this.api}/admin/sites/${encodeURIComponent(siteKey)}/write-key`, null);
    }

    rotateWriteKey(siteKey: string): Observable<WriteKeyResult> {
        return this.http.post<WriteKeyResult>(`${this.api}/admin/sites/${encodeURIComponent(siteKey)}/write-key/rotate`, null);
    }

    revokeWriteKey(siteKey: string): Observable<void> {
        return this.http.delete<void>(`${this.api}/admin/sites/${encodeURIComponent(siteKey)}/write-key`);
    }

    shareSite(siteKey: string): Observable<{ token: string }> {
        return this.http.post<{ token: string }>(`${this.api}/sites/${encodeURIComponent(siteKey)}/share`, {});
    }
}
