import { Component, HostListener, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AppNotification, NotificationService, NotificationSeverity } from '@app/shared/services/notification.service';

/** "9+" above 9, nothing at 0. */
export function badgeLabel(count: number): string {
    if (count <= 0) return '';
    return count > 9 ? '9+' : String(count);
}

/** "just now", "5m ago", "3h ago", "2d ago", then the date. */
export function relativeTime(iso: string, now: Date = new Date()): string {
    const seconds = Math.max(0, Math.round((now.getTime() - new Date(iso).getTime()) / 1000));
    if (seconds < 60) return 'just now';
    const minutes = Math.floor(seconds / 60);
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours}h ago`;
    const days = Math.floor(hours / 24);
    if (days < 7) return `${days}d ago`;
    return new Date(iso).toLocaleDateString();
}

/** Only routes of the app are followed: anything else (an absolute URL) is ignored. */
export function isInternalLink(link: string | null | undefined): link is string {
    return !!link && link.startsWith('/') && !link.startsWith('//');
}

@Component({
    selector: 'app-notification-bell',
    standalone: true,
    templateUrl: './notification-bell.html',
})
export class NotificationBellComponent {
    private readonly router = inject(Router);
    readonly notifications = inject(NotificationService);
    readonly Severity = NotificationSeverity;

    readonly open = signal(false);
    readonly badge = () => badgeLabel(this.notifications.unreadCount());
    readonly relativeTime = relativeTime;

    toggle(): void {
        const opening = !this.open();
        this.open.set(opening);
        if (opening) void this.notifications.load();
    }

    close(): void {
        this.open.set(false);
    }

    async select(notification: AppNotification): Promise<void> {
        void this.notifications.markRead(notification.id);
        if (isInternalLink(notification.link)) {
            this.close();
            await this.router.navigateByUrl(notification.link);
        }
    }

    markAllRead(): void {
        void this.notifications.markAllRead();
    }

    @HostListener('document:click')
    onDocumentClick(): void {
        this.close();
    }
}
