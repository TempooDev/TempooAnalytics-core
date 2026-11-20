import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import template from './notification-bell.html?raw';
import headerTemplate from '../header.html?raw';
import { NotificationBellComponent, badgeLabel, isInternalLink, relativeTime } from './notification-bell';
import { AppNotification, NotificationService, NotificationSeverity } from '@app/shared/services/notification.service';

const notification = (extra: Partial<AppNotification> = {}): AppNotification => ({
    id: 'n1', type: 'import.failed', severity: NotificationSeverity.Error, title: 'Import failed', body: 'bad file',
    link: '/account/sites/s1/imports', siteId: 's1', createdAt: '2026-10-23T10:00:00Z', readAt: null, ...extra
});

describe('NotificationBellComponent', () => {
    let service: { unreadCount: ReturnType<typeof signal<number>>; items: ReturnType<typeof signal<AppNotification[]>>; loading: ReturnType<typeof signal<boolean>>; load: ReturnType<typeof vi.fn>; markRead: ReturnType<typeof vi.fn>; markAllRead: ReturnType<typeof vi.fn> };
    let navigateByUrl: ReturnType<typeof vi.fn>;

    function create(): NotificationBellComponent {
        service = {
            unreadCount: signal(0), items: signal<AppNotification[]>([]), loading: signal(false),
            load: vi.fn().mockResolvedValue(undefined), markRead: vi.fn().mockResolvedValue(undefined), markAllRead: vi.fn().mockResolvedValue(undefined)
        };
        navigateByUrl = vi.fn().mockResolvedValue(true);
        TestBed.configureTestingModule({
            providers: [
                provideZonelessChangeDetection(),
                { provide: NotificationService, useValue: service },
                { provide: Router, useValue: { navigateByUrl } }
            ]
        });
        return TestBed.runInInjectionContext(() => new NotificationBellComponent());
    }

    afterEach(() => TestBed.resetTestingModule());

    it('shows the unread count, "9+" above 9 and nothing at 0', () => {
        expect(badgeLabel(0)).toBe('');
        expect(badgeLabel(3)).toBe('3');
        expect(badgeLabel(9)).toBe('9');
        expect(badgeLabel(10)).toBe('9+');

        const bell = create();
        service.unreadCount.set(12);
        expect(bell.badge()).toBe('9+');
        expect(template).toContain('data-testid="notification-badge"');
    });

    it('loads the latest notifications when opened', () => {
        const bell = create();

        bell.toggle();

        expect(bell.open()).toBe(true);
        expect(service.load).toHaveBeenCalledTimes(1);

        bell.toggle();
        expect(bell.open()).toBe(false);
        expect(service.load).toHaveBeenCalledTimes(1);
    });

    it('marks a notification as read and follows its link', async () => {
        const bell = create();
        bell.toggle();

        await bell.select(notification());

        expect(service.markRead).toHaveBeenCalledWith('n1');
        expect(navigateByUrl).toHaveBeenCalledWith('/account/sites/s1/imports');
        expect(bell.open()).toBe(false);
    });

    it('marks a notification without link as read and stays open', async () => {
        const bell = create();
        bell.toggle();

        await bell.select(notification({ link: null }));

        expect(service.markRead).toHaveBeenCalledWith('n1');
        expect(navigateByUrl).not.toHaveBeenCalled();
        expect(bell.open()).toBe(true);
    });

    it('never follows external links', async () => {
        expect(isInternalLink('/account/sites/s1/imports')).toBe(true);
        expect(isInternalLink('https://evil.example')).toBe(false);
        expect(isInternalLink('//evil.example')).toBe(false);

        const bell = create();
        await bell.select(notification({ link: 'https://evil.example' }));

        expect(navigateByUrl).not.toHaveBeenCalled();
    });

    it('marks all as read', () => {
        const bell = create();

        bell.markAllRead();

        expect(service.markAllRead).toHaveBeenCalledTimes(1);
        expect(template).toContain('Mark all as read');
    });

    it('has an empty state and an icon per severity', () => {
        expect(template).toContain('data-testid="notifications-empty"');
        expect(template).toContain("You're all caught up");
        for (const severity of ['Severity.Success', 'Severity.Error', 'Severity.Warning']) {
            expect(template).toContain(`@case (${severity})`);
        }
    });

    it('shows relative dates', () => {
        const now = new Date('2026-10-23T12:00:00Z');
        expect(relativeTime('2026-10-23T11:59:30Z', now)).toBe('just now');
        expect(relativeTime('2026-10-23T11:55:00Z', now)).toBe('5m ago');
        expect(relativeTime('2026-10-23T09:00:00Z', now)).toBe('3h ago');
        expect(relativeTime('2026-10-21T12:00:00Z', now)).toBe('2d ago');
    });

    it('is only in the header for a signed-in user with the flag on', () => {
        expect(headerTemplate).toContain('@if (token() && notificationsEnabled())');
        expect(headerTemplate.indexOf('<app-notification-bell />')).toBeLessThan(headerTemplate.indexOf('(click)="addSite()"'));
        expect(headerTemplate.indexOf('<app-notification-bell />')).toBeGreaterThan(headerTemplate.indexOf('themeService.toggleTheme()'));
    });
});
