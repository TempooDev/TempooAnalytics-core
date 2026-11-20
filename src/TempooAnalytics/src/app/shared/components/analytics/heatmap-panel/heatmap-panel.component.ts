import { ChangeDetectionStrategy, Component, Input, computed, effect, inject, signal } from '@angular/core';
import { DecimalPipe, NgComponentOutlet } from '@angular/common';
import { UPSELL_COMPONENT } from '@app/core/upsell';
import { firstValueFrom } from 'rxjs';
import { Heatmap, HeatmapDevice, HeatmapPage, HeatmapService } from '@shared/services/heatmap.service';
import { ToastService } from '@app/shared/services/toast.service';

@Component({
    selector: 'ta-heatmap-panel',
    standalone: true,
    imports: [DecimalPipe, NgComponentOutlet],
    templateUrl: './heatmap-panel.component.html',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class HeatmapPanelComponent {
    readonly upsell = inject(UPSELL_COMPONENT);
    private heatmaps = inject(HeatmapService);
    private toast = inject(ToastService);

    // Decorator inputs backed by signals: the Vitest setup compiles components in JIT, where signal inputs
    // cannot be set.
    readonly siteId = signal('');
    /** True when the user's plan lacks advanced analytics: shows the upgrade notice and loads nothing. */
    readonly locked = signal(false);

    @Input({ alias: 'siteId', required: true }) set siteIdInput(value: string) { this.siteId.set(value); }
    @Input({ alias: 'locked' }) set lockedInput(value: boolean) { this.locked.set(value); }

    readonly devices: { value: HeatmapDevice; label: string }[] = [
        { value: 'desktop', label: 'Desktop' },
        { value: 'tablet', label: 'Tablet' },
        { value: 'mobile', label: 'Mobile' }
    ];
    readonly ranges = [7, 30, 90];

    device = signal<HeatmapDevice>('desktop');
    days = signal(30);
    pages = signal<HeatmapPage[]>([]);
    selectedPath = signal<string | null>(null);
    heatmap = signal<Heatmap | null>(null);
    loading = signal(false);
    openingOverlay = signal(false);

    frustratedElements = computed(() =>
        (this.heatmap()?.topElements ?? []).filter(e => e.rageClicks > 0 || e.deadClicks > 0));

    constructor() {
        effect(() => {
            const siteId = this.siteId();
            const device = this.device();
            const days = this.days();
            if (!siteId || this.locked()) return;
            void this.loadPages(siteId, device, days);
        });
    }

    private async loadPages(siteId: string, device: HeatmapDevice, days: number) {
        this.loading.set(true);
        try {
            const pages = await firstValueFrom(this.heatmaps.getPages({ siteId, device, days }));
            this.pages.set(pages);
            const keep = pages.find(p => p.path === this.selectedPath())?.path ?? pages[0]?.path ?? null;
            this.selectedPath.set(keep);
            this.heatmap.set(keep ? await firstValueFrom(this.heatmaps.getHeatmap({ siteId, device, days }, keep)) : null);
        } catch {
            this.pages.set([]);
            this.heatmap.set(null);
        } finally {
            this.loading.set(false);
        }
    }

    async selectPage(path: string) {
        if (path === this.selectedPath()) return;
        this.selectedPath.set(path);
        try {
            this.heatmap.set(await firstValueFrom(this.heatmaps.getHeatmap({ siteId: this.siteId(), device: this.device(), days: this.days() }, path)));
        } catch {
            this.heatmap.set(null);
        }
    }

    async viewOnPage() {
        const path = this.selectedPath();
        if (!path || this.openingOverlay()) return;
        // Opened before the request so popup blockers treat it as the click's own window.
        const tab = window.open('', '_blank');
        // The customer's page must not get a handle back to the app.
        if (tab) tab.opener = null;
        this.openingOverlay.set(true);
        try {
            const grant = await firstValueFrom(this.heatmaps.createOverlayToken(this.siteId(), path));
            if (!grant.siteUrl) {
                tab?.close();
                this.toast.error('Add your site domain to the allowed origins to view the heatmap on the page.');
                return;
            }
            const url = this.heatmaps.overlayUrl(grant.siteUrl, path, grant.token);
            if (tab) tab.location.href = url;
            else window.location.href = url;
        } catch {
            tab?.close();
            this.toast.error('The heatmap could not be opened. Please try again.');
        } finally {
            this.openingOverlay.set(false);
        }
    }
}
