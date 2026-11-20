import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal, effect } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ProgressBarModule } from 'primeng/progressbar';
import { MessageModule } from 'primeng/message';
import { AutoCompleteModule } from 'primeng/autocomplete';
import { ButtonModule } from 'primeng/button';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TableModule } from 'primeng/table';
import { InputTextModule } from 'primeng/inputtext';
import { AnalyticsService, SiteSummary, CustomEventGroupResult } from '@app/shared/services';
import { firstValueFrom } from 'rxjs';

interface FilterRow { key: string; value: string; }

@Component({
    selector: 'app-custom-events-groups',
    imports: [
        CommonModule,
        FormsModule,
        ProgressBarModule,
        MessageModule,
        AutoCompleteModule,
        ButtonModule,
        ProgressSpinnerModule,
        TableModule,
        InputTextModule
    ],
    templateUrl: './custom-events-groups.page.html',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class CustomEventsGroupsPageComponent {
    private readonly analytics = inject(AnalyticsService);

    // 1. Sites Resource
    sitesResource = this.analytics.sitesResource;
    sites = computed(() => this.sitesResource.value() || []);
    loadingSites = computed(() => this.sitesResource.isLoading());

    protected readonly selectedSiteKey = signal<string>('');
    protected readonly selectedSite = signal<SiteSummary | null>(null);
    protected readonly filteredSites = signal<SiteSummary[]>([]);
    protected readonly eventName = signal<string>('cta_click');
    protected readonly days = signal<number>(7);
    protected readonly groupKeysText = signal<string>('button,color');
    protected readonly filterRows = signal<FilterRow[]>([]);

    protected readonly querying = signal(false);
    protected readonly error = signal<string | null>(null);

    protected readonly results = signal<CustomEventGroupResult[]>([]);
    protected readonly displayedKeys = signal<string[]>([]);

    constructor() {
        // Auto-select first site when loaded
        effect(() => {
            const res = this.sites();
            if (res && res.length > 0 && !this.selectedSiteKey()) {
                this.selectedSite.set(res[0]);
                this.selectedSiteKey.set(res[0].siteKey);
            }
        });
    }

    addFilterRow() {
        this.filterRows.update(rows => [...rows, { key: '', value: '' }]);
    }

    removeFilterRow(i: number) {
        this.filterRows.update(rows => rows.filter((_, idx) => idx !== i));
    }

    clearFilters() {
        this.filterRows.set([]);
    }

    adoptRow(r: CustomEventGroupResult) {
        this.filterRows.set(Object.entries(r.keys).map(([k, v]) => ({ key: k, value: v })));
    }

    async query() {
        this.error.set(null);
        const siteKey = this.selectedSiteKey();
        const evtName = this.eventName();
        const daysValue = this.days();

        if (!siteKey) {
            this.error.set('Select a site');
            return;
        }
        if (!evtName) {
            this.error.set('Enter event name');
            return;
        }

        const rawKeys = this.groupKeysText().split(',').map(k => k.trim()).filter(k => !!k);
        if (!rawKeys.length) {
            this.error.set('Enter at least one grouping key');
            return;
        }

        const filters: Record<string, string> = {};
        for (const fr of this.filterRows()) {
            if (fr.key && fr.value) filters[fr.key] = fr.value;
        }

        this.querying.set(true);
        this.results.set([]);
        this.displayedKeys.set([]);

        try {
            const res: CustomEventGroupResult[] = await firstValueFrom(this.analytics.getGroupedCustomEvents(siteKey, evtName, rawKeys, daysValue, filters));
            this.results.set(res || []);
            const allKeysSet = new Set<string>();
            for (const r of (res || [])) {
                Object.keys(r.keys).forEach(k => allKeysSet.add(k));
            }
            const extraKeys = Array.from(allKeysSet).filter(k => !rawKeys.includes(k)).sort();
            this.displayedKeys.set([...rawKeys, ...extraKeys]);
        } catch (err: any) {
            this.error.set(err?.message || 'Query failed');
        } finally {
            this.querying.set(false);
        }
    }

    copyRow(r: CustomEventGroupResult) {
        const text = JSON.stringify(r, null, 2);
        navigator.clipboard.writeText(text).catch(() => { });
    }

    filterSites(event: any) {
        const query = (event.query || '').toLowerCase();
        const allSites = this.sites();
        this.filteredSites.set(!query
            ? [...allSites]
            : allSites.filter(s => s.name.toLowerCase().includes(query) || s.siteKey.toLowerCase().includes(query))
        );
    }

    onSiteSelect(event: any) {
        if (event && event.value) {
            this.selectedSite.set(event.value);
        }
        const site = this.selectedSite();
        if (site) {
            this.selectedSiteKey.set(site.siteKey);
        }
    }
}
