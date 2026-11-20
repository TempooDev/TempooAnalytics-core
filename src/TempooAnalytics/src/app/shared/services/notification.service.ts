import { DOCUMENT } from '@angular/common';
import { HttpClient, HttpContext } from '@angular/common/http';
import { DestroyRef, Injectable, effect, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { SILENT_ERRORS } from '@app/shared/interceptors/error.interceptor';
import { AuthService } from './auth.service';
import { FeatureFlagsService } from './feature-flags.service';

/** Matches NotificationSeverity in the API. */
export enum NotificationSeverity {
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}

export interface AppNotification {
    id: string;
    type: string;
    severity: NotificationSeverity;
    title: string;
    body: string | null;
    /** Route of the app, never an external URL. */
    link: string | null;
    siteId: string | null;
    createdAt: string;
    readAt: string | null;
}

export interface NotificationPage {
    items: AppNotification[];
    nextCursor: string | null;
}

export const NOTIFICATIONS_POLL_INTERVAL_MS = 60_000;
export const NOTIFICATIONS_PAGE_SIZE = 20;

/**
 * Notification inbox of the signed-in user. While signed in (and with the notifications flag on) the unread
 * count is refreshed every 60 s when the tab is visible, and again when the tab becomes visible.
 * There is no real-time push.
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
    private readonly http = inject(HttpClient);
    private readonly auth = inject(AuthService);
    private readonly flags = inject(FeatureFlagsService);
    private readonly document = inject(DOCUMENT);
    private readonly api = '/api/notifications';
    private readonly silent = () => new HttpContext().set(SILENT_ERRORS, true);
    private timer: ReturnType<typeof setInterval> | null = null;

    readonly unreadCount = signal(0);
    readonly items = signal<AppNotification[]>([]);
    readonly loading = signal(false);

    private readonly onVisibilityChange = () => {
        if (this.timer && this.isVisible()) void this.refreshCount();
    };

    constructor() {
        this.document.addEventListener('visibilitychange', this.onVisibilityChange);
        inject(DestroyRef).onDestroy(() => {
            this.stop();
            this.document.removeEventListener('visibilitychange', this.onVisibilityChange);
        });

        // Polls only for a signed-in user with the feature on; stops on logout.
        effect(() => {
            if (this.auth.tokenSignal()() && this.flags.notifications()) this.start();
            else this.stop();
        });
    }

    get polling(): boolean {
        return this.timer !== null;
    }

    start(): void {
        if (this.timer) return;
        void this.refreshCount();
        this.timer = setInterval(() => {
            if (this.isVisible()) void this.refreshCount();
        }, NOTIFICATIONS_POLL_INTERVAL_MS);
    }

    stop(): void {
        if (this.timer) clearInterval(this.timer);
        this.timer = null;
        this.unreadCount.set(0);
        this.items.set([]);
    }

    async refreshCount(): Promise<void> {
        try {
            const res = await firstValueFrom(this.http.get<{ count: number }>(`${this.api}/unread-count`, { context: this.silent() }));
            this.unreadCount.set(res?.count ?? 0);
        } catch {
            // Keep the last count; the next tick tries again.
        }
    }

    /** Loads the latest notifications (when the bell is opened). */
    async load(): Promise<void> {
        this.loading.set(true);
        try {
            const page = await firstValueFrom(this.http.get<NotificationPage>(this.api, { params: { limit: NOTIFICATIONS_PAGE_SIZE } }));
            this.items.set(page?.items ?? []);
        } catch {
            // The error interceptor already told the user.
        } finally {
            this.loading.set(false);
        }
    }

    async markRead(id: string): Promise<void> {
        const item = this.items().find(n => n.id === id);
        if (item && !item.readAt) {
            const now = new Date().toISOString();
            this.items.update(list => list.map(n => (n.id === id ? { ...n, readAt: now } : n)));
            this.unreadCount.update(c => Math.max(0, c - 1));
        }
        try {
            await firstValueFrom(this.http.post<void>(`${this.api}/${id}/read`, null));
        } catch {
            await this.refreshCount();
        }
    }

    async markAllRead(): Promise<void> {
        const now = new Date().toISOString();
        this.items.update(list => list.map(n => (n.readAt ? n : { ...n, readAt: now })));
        this.unreadCount.set(0);
        try {
            await firstValueFrom(this.http.post<void>(`${this.api}/read-all`, null));
        } catch {
            await this.refreshCount();
        }
    }

    private isVisible(): boolean {
        return this.document.visibilityState !== 'hidden';
    }
}
