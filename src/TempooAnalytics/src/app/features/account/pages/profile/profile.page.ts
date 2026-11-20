import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule, NgComponentOutlet } from '@angular/common';
import { AuthService } from '@app/shared/services/auth.service';
import { AnalyticsService } from '@app/shared/services/analytics.service';
import { FeatureFlagsService } from '@app/shared/services/feature-flags.service';
import { PROFILE_EXTENSIONS } from '@app/core/profile-extensions';
import { ENTITLEMENTS } from '@app/core/entitlements';
import { UPSELL_COMPONENT } from '@app/core/upsell';

@Component({
    selector: 'app-profile-page',
    standalone: true,
    imports: [CommonModule, NgComponentOutlet],
    templateUrl: './profile.page.html',
})
export class ProfilePage implements OnInit {
    private auth = inject(AuthService);
    private analytics = inject(AnalyticsService);
    private entitlements = inject(ENTITLEMENTS);
    featureFlags = inject(FeatureFlagsService);

    /** Blocks contributed by feature packages, ordered and filtered by their own visibility rule. */
    private extensionRegistry = inject(PROFILE_EXTENSIONS, { optional: true });
    extensions = computed(() => (this.extensionRegistry ?? []).flat()
        .filter(e => !e.visible || e.visible(this.featureFlags))
        .sort((a, b) => (a.order ?? 100) - (b.order ?? 100)));
    /** Rendered above the usage statistics (e.g. the subscription card). */
    topExtensions = computed(() => this.extensions().filter(e => e.placement === 'top'));
    /** Rendered after the usage statistics. */
    bottomExtensions = computed(() => this.extensions().filter(e => e.placement !== 'top'));

    upsell = inject(UPSELL_COMPONENT);
    profile = this.auth.userProfile;
    events30 = signal<number>(0);
    loading = computed(() => this.auth.userResource.isLoading());

    planLimit = computed(() => this.entitlements.features().maxMonthlyEvents);
    /** False for builds without plans (self-host): there is no quota to show. */
    hasQuota = computed(() => this.planLimit() < Number.MAX_SAFE_INTEGER);

    usagePercentage = computed(() => {
        const current = this.events30();
        const limit = this.planLimit();
        if (!limit) return 0;
        return Math.min(Math.round((current / limit) * 100), 100);
    });

    async ngOnInit() {
        try {
            // Dashboard data still needs manual trigger or its own resource
            const dashboard = await this.analytics.getDashboard(30).toPromise();
            this.events30.set(dashboard?.metrics30?.events30 || 0);
        } catch (error) {
            console.error('Failed to load dashboard metrics', error);
        }
    }
}
