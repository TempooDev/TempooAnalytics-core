import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class FeedbackService {
    private readonly http = inject(HttpClient);
    private api = '/api/feedback';

    sendFeedback(title: string, description: string): Observable<void> {
        return this.http.post<void>(this.api, { title, description });
    }
}
