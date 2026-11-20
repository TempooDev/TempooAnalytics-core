import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export interface FeatureFlags {
    plausibleImport: boolean;
    ga4Import: boolean;
    notifications: boolean;
    heatmaps: boolean;
    /** Flags contributed by feature packages (cloud modules) arrive under their own names. */
    [name: string]: boolean;
}

/**
 * Features that are deployed but not released yet, read at runtime from GET /api/features.
 * Everything stays off until the API says otherwise (also when the request fails).
 */
@Injectable({ providedIn: 'root' })
export class FeatureFlagsService {
    private readonly http = inject(HttpClient);
    private loading?: Promise<void>;

    readonly plausibleImport = signal(false);
    readonly ga4Import = signal(false);
    readonly notifications = signal(false);
    readonly heatmaps = signal(false);
    /** Flags the core does not know by name (e.g. from the cloud modules). */
    private readonly extra = signal<Record<string, boolean>>({});

    /**
     * False when the API says this server has no object storage (S3/R2): imports and exports cannot work there.
     * The API only reports `dataStorageUnavailable` in that case, so everything else (the hosted service included) stays on.
     */
    readonly dataStorage = computed(() => !this.extra()['dataStorageUnavailable']);

    /** Reactive read of any flag by its API name; false when unknown or while loading. */
    isOn(name: string): boolean {
        return !!this.extra()[name];
    }

    constructor() {
        void this.load();
    }

    load(): Promise<void> {
        this.loading ??= firstValueFrom(this.http.get<FeatureFlags>('/api/features'))
            .then(flags => {
                this.plausibleImport.set(!!flags?.plausibleImport);
                this.ga4Import.set(!!flags?.ga4Import);
                this.notifications.set(!!flags?.notifications);
                this.heatmaps.set(!!flags?.heatmaps);
                this.extra.set(Object.fromEntries(Object.entries(flags ?? {}).map(([k, v]) => [k, !!v])));
            })
            .catch(() => {
                this.plausibleImport.set(false);
                this.ga4Import.set(false);
                this.notifications.set(false);
                this.heatmaps.set(false);
                this.extra.set({});
            });
        return this.loading;
    }
}
