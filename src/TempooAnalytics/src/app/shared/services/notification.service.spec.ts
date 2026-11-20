import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { vi } from 'vitest';
import { AuthService } from './auth.service';
import { FeatureFlagsService } from './feature-flags.service';
import { AppNotification, NOTIFICATIONS_POLL_INTERVAL_MS, NotificationService, NotificationSeverity } from './notification.service';

const notification = (id: string, extra: Partial<AppNotification> = {}): AppNotification => ({
    id, type: 'import.completed', severity: NotificationSeverity.Success, title: 'Import completed', body: null,
    link: '/account/sites/s1/imports', siteId: 's1', createdAt: '2026-10-23T10:00:00Z', readAt: null, ...extra
});

describe('NotificationService', () => {
    let http: HttpTestingController;
    const token = signal<string | null>('token');
    const enabled = signal(true);
    let visibility: DocumentVisibilityState;

    function create(): NotificationService {
        const service = TestBed.inject(NotificationService);
        TestBed.tick(); // runs the effect that starts polling
        return service;
    }

    const countRequests = () => http.match('/api/notifications/unread-count');
    const settle = async () => { for (let i = 0; i < 5; i++) await Promise.resolve(); };

    beforeEach(() => {
        vi.useFakeTimers();
        token.set('token');
        enabled.set(true);
        visibility = 'visible';
        vi.spyOn(document, 'visibilityState', 'get').mockImplementation(() => visibility);
        TestBed.configureTestingModule({
            providers: [
                provideZonelessChangeDetection(),
                provideHttpClient(),
                provideHttpClientTesting(),
                { provide: AuthService, useValue: { tokenSignal: () => token } },
                { provide: FeatureFlagsService, useValue: { notifications: enabled } }
            ]
        });
        http = TestBed.inject(HttpTestingController);
    });

    afterEach(() => {
        TestBed.resetTestingModule();
        vi.restoreAllMocks();
        vi.useRealTimers();
    });

    it('loads the unread count at start and every 60 s', async () => {
        const service = create();
        countRequests().forEach(r => r.flush({ count: 2 }));
        await settle();
        expect(service.unreadCount()).toBe(2);

        vi.advanceTimersByTime(NOTIFICATIONS_POLL_INTERVAL_MS);
        const next = countRequests();
        expect(next.length).toBe(1);
        next[0].flush({ count: 5 });
        await settle();
        expect(service.unreadCount()).toBe(5);
    });

    it('does not poll while the tab is hidden, and refreshes when it becomes visible', () => {
        create();
        countRequests().forEach(r => r.flush({ count: 0 }));

        visibility = 'hidden';
        vi.advanceTimersByTime(NOTIFICATIONS_POLL_INTERVAL_MS * 3);
        expect(countRequests().length).toBe(0);

        visibility = 'visible';
        document.dispatchEvent(new Event('visibilitychange'));
        expect(countRequests().length).toBe(1);
    });

    it('does not poll with the flag off', () => {
        enabled.set(false);
        const service = create();

        vi.advanceTimersByTime(NOTIFICATIONS_POLL_INTERVAL_MS * 2);

        expect(service.polling).toBe(false);
        http.expectNone('/api/notifications/unread-count');
    });

    it('stops polling and clears the inbox on logout', () => {
        const service = create();
        countRequests().forEach(r => r.flush({ count: 3 }));

        token.set(null);
        TestBed.tick();
        vi.advanceTimersByTime(NOTIFICATIONS_POLL_INTERVAL_MS * 2);

        expect(service.polling).toBe(false);
        expect(service.unreadCount()).toBe(0);
        expect(countRequests().length).toBe(0);
    });

    it('loads the latest 20 notifications', async () => {
        const service = create();
        countRequests().forEach(r => r.flush({ count: 1 }));

        const loading = service.load();
        const req = http.expectOne(r => r.url === '/api/notifications');
        expect(req.request.params.get('limit')).toBe('20');
        req.flush({ items: [notification('a')], nextCursor: null });
        await loading;

        expect(service.items().map(n => n.id)).toEqual(['a']);
    });

    it('marks one as read and lowers the count', async () => {
        const service = create();
        countRequests().forEach(r => r.flush({ count: 2 }));
        await settle();
        service.items.set([notification('a'), notification('b')]);

        const marking = service.markRead('a');
        http.expectOne({ method: 'POST', url: '/api/notifications/a/read' }).flush(null);
        await marking;

        expect(service.unreadCount()).toBe(1);
        expect(service.items().find(n => n.id === 'a')!.readAt).not.toBeNull();
        expect(service.items().find(n => n.id === 'b')!.readAt).toBeNull();
    });

    it('does not lower the count when the notification was already read', async () => {
        const service = create();
        countRequests().forEach(r => r.flush({ count: 1 }));
        await settle();
        service.items.set([notification('a', { readAt: '2026-10-23T11:00:00Z' })]);

        const marking = service.markRead('a');
        http.expectOne('/api/notifications/a/read').flush(null);
        await marking;

        expect(service.unreadCount()).toBe(1);
    });

    it('marks all as read', async () => {
        const service = create();
        countRequests().forEach(r => r.flush({ count: 2 }));
        await settle();
        service.items.set([notification('a'), notification('b')]);

        const marking = service.markAllRead();
        http.expectOne({ method: 'POST', url: '/api/notifications/read-all' }).flush(null);
        await marking;

        expect(service.unreadCount()).toBe(0);
        expect(service.items().every(n => n.readAt)).toBe(true);
    });
});
