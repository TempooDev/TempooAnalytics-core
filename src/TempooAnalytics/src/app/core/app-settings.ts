import { InjectionToken } from '@angular/core';

/**
 * Instance-level settings the UI needs and that must never be baked in as a production host or a person's address.
 * The core derives them from the instance itself (its own origin) and from an optional runtime file; a feature package
 * (the cloud build) overrides them with its own values.
 */
export interface AppSettings {
    /** Name shown in the header. */
    brandName: string;
    /** Link of the "Analytics by" credit of the public preview; empty = plain text, no link. */
    brandUrl: string;
    /** Contact address of the operator; empty hides the contact lines (no mailto is rendered). */
    supportEmail: string;
    /** Absolute URL of the tracker script (analytics.js) the snippets point at. */
    trackingScriptUrl: string;
    /** Value of `data-api-url` in the snippet; empty = the attribute is omitted and the tracker uses its own default. */
    trackingApiUrl: string;
}

/** Optional runtime values the operator can set without rebuilding: `window.__TEMPOO_CONFIG__` (see public/config.js). */
export interface RuntimeConfig {
    brandName?: string;
    supportEmail?: string;
}

declare global {
    interface Window {
        __TEMPOO_CONFIG__?: RuntimeConfig;
    }
}

export const DEFAULT_BRAND_NAME = 'Tempoo Analytics';

/** Settings of a self-hosted instance: everything is derived from where the app is served. */
export function instanceSettings(win: Pick<Window, 'location' | '__TEMPOO_CONFIG__'> = window): AppSettings {
    const origin = win.location.origin;
    const runtime = win.__TEMPOO_CONFIG__ ?? {};
    return {
        brandName: runtime.brandName?.trim() || DEFAULT_BRAND_NAME,
        brandUrl: '',
        supportEmail: runtime.supportEmail?.trim() || '',
        trackingScriptUrl: `${origin}/assets/analytics.js`,
        trackingApiUrl: `${origin}/analytics/events`
    };
}

export const APP_SETTINGS = new InjectionToken<AppSettings>('APP_SETTINGS', {
    providedIn: 'root',
    factory: () => instanceSettings()
});

/** The `data-api-url` attribute of the snippet, or nothing when the settings carry no API URL. */
export function apiUrlAttribute(settings: AppSettings): string {
    return settings.trackingApiUrl ? ` data-api-url="${settings.trackingApiUrl}"` : '';
}
