import { Component, inject, signal, ChangeDetectionStrategy, OnInit, computed, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ENTITLEMENTS } from '@app/core/entitlements';
import { AnalyticsService, SiteSummary } from '@app/shared/services/analytics.service';
import { ToastService } from '@app/shared/services/toast.service';
import { FeatureFlagsService } from '@app/shared/services/feature-flags.service';
import { HttpClient } from '@angular/common/http';
import { APP_SETTINGS, apiUrlAttribute } from '@app/core/app-settings';
import { UPSELL_COMPONENT } from '@app/core/upsell';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { EmptyStateComponent } from '@shared/components';
import { firstValueFrom } from 'rxjs';

@Component({
    standalone: true,
    imports: [CommonModule, FormsModule, EmptyStateComponent, RouterLink],
    templateUrl: './account-sites.page.html',
    styleUrls: ['./account-sites.page.css'],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccountSitesPage implements OnInit {
    private analytics = inject(AnalyticsService);
    private toast = inject(ToastService);
    private http = inject(HttpClient);
    private route = inject(ActivatedRoute);
    private router = inject(Router);
    private featureFlags = inject(FeatureFlagsService);
    private api = '';

    // Resource from service
    sitesResource = this.analytics.sitesResource;
    
    private _stickySites = signal<SiteSummary[]>([]);
    sites = this._stickySites.asReadonly();
    
    loading = computed(() => this.sitesResource.isLoading());
    hasData = computed(() => this.sites().length > 0);
    showInitialLoading = computed(() => this.loading() && !this.hasData());
    
    error = signal<string | null>(null);
    searchQuery = signal('');
    showAddModal = signal(false);

    private entitlements = inject(ENTITLEMENTS);
    private settings = inject(APP_SETTINGS);
    upsell = inject(UPSELL_COMPONENT);
    planFeatures = this.entitlements.features;
    plausibleImportEnabled = this.featureFlags.plausibleImport;
    /** True when the server has no object storage: importing is disabled with an explanation. */
    storageMissing = computed(() => !this.featureFlags.dataStorage());

    openImports(site: SiteSummary) {
        // Importing history is a paid feature, like exports.
        if (this.entitlements.isFree()) {
            this.toast.info(this.entitlements.planHint('import'));
            this.router.navigate(['/account/profile']);
            return;
        }
        this.router.navigate(['/account/sites', site.siteId, 'imports']);
    }
    /** False when the build puts no cap on sites (community): the usage counter is not shown then. */
    hasSiteLimit = computed(() => this.planFeatures().maxSites < Number.MAX_SAFE_INTEGER);
    isLimitReached = computed(() => this.sites().length >= this.planFeatures().maxSites);

    filteredSites = computed(() => {
        const q = this.searchQuery().toLowerCase();
        const all = this.sites();
        if (!q) return all;
        return all.filter(s =>
            s.name.toLowerCase().includes(q) ||
            s.siteKey.toLowerCase().includes(q)
        );
    });

    newSiteName = signal('');
    newSiteOrigins = signal('');
    addingSite = signal(false);

    constructor() {
        effect(() => {
            const val = this.sitesResource.value();
            if (val) this._stickySites.set(val);
        });
    }

    ngOnInit() {
        this.route.queryParams.subscribe(params => {
            if (params['action'] === 'add') {
                this.openAddModal();
                // Clear query param
                this.router.navigate([], {
                    relativeTo: this.route,
                    queryParams: { action: null },
                    queryParamsHandling: 'merge',
                    replaceUrl: true
                });
            }
        });
    }

    onSearchChange(query: string) {
        this.searchQuery.set(query);
    }

    openAddModal() {
        if (this.isLimitReached()) {
            this.toast.show(this.entitlements.planHint('sites'));
            this.router.navigate(['/account/profile']);
            return;
        }
        this.showAddModal.set(true);
        this.newSiteName.set('');
        this.newSiteOrigins.set('');
    }

    closeAddModal() {
        this.showAddModal.set(false);
    }

    async addSite() {
        const name = this.newSiteName().trim();
        const originsInput = this.newSiteOrigins().trim();

        if (!name || !originsInput) {
            this.error.set('Site name and URL are required');
            return;
        }

        this.addingSite.set(true);
        this.error.set(null);

        try {
            const origins = originsInput
                .split(',')
                .map(o => o.trim())
                .filter(o => o.length > 0);

            if (origins.length === 0) {
                this.error.set('At least one valid URL is required');
                return;
            }

            await firstValueFrom(this.http.post<any>(`${this.api}/api/admin/sites`, {
                name,
                allowedOrigins: origins
            }));

            this.sitesResource.reload();
            this.toast.show('Site created successfully!');
            this.closeAddModal();
        } catch (err) {
            this.error.set('Failed to create site. Please try again.');
        } finally {
            this.addingSite.set(false);
        }
    }

    copyToClipboard(text: string) {
        navigator.clipboard.writeText(text);
        this.toast.show('Copied to clipboard!');
    }

    async deleteSite(site: SiteSummary) {
        if (!confirm(`Are you sure you want to delete ${site.name}?`)) return;

        try {
            await firstValueFrom(this.analytics.deleteSite(site.siteKey));
            this.toast.show(`${site.name} deleted successfully.`);
            this.sitesResource.reload();
        } catch (err) {
            this.toast.show('Failed to delete site. Please try again.');
        }
    }

    getTrackingScript(site: SiteSummary): string {
        return `<script src="${this.settings.trackingScriptUrl}" data-site="${site.siteKey}" data-key="${site.writeKey}"${apiUrlAttribute(this.settings)}></script>`;
    }
}
