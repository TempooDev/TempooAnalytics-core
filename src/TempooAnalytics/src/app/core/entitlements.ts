import { InjectionToken, Signal, signal } from '@angular/core';

/** Limits and capabilities of the signed-in user's plan. */
export interface PlanFeatures {
    maxMonthlyEvents: number;
    maxSites: number;
    /** Lead identification panels. */
    leads: boolean;
    /** Filters, custom events, funnels and heatmaps. */
    advancedAnalytics: boolean;
}

/**
 * What the current user's plan allows. The core ships a default with no limits and no plan (self-host);
 * a feature package (the cloud build) overrides it with its subscription data.
 */
export interface Entitlements {
    /** True when the build sells plans (the cloud build): only then are plan limits and upgrade prompts shown. */
    hasPlans: boolean;
    /** Plan name to show; null when the build has no plans (nothing is rendered). */
    plan: Signal<string | null>;
    /** True when paid-only actions (exports, history imports) must be refused and the user sent to upgrade. */
    isFree: Signal<boolean>;
    features: Signal<PlanFeatures>;
    /** Wording for refusing a paid-only action; empty when the build has no plans (the action is never refused there). */
    planHint: (action: PlanAction) => string;
}

export type PlanAction = 'export' | 'import' | 'import-denied' | 'sites';

export const UNLIMITED_FEATURES: PlanFeatures = {
    maxMonthlyEvents: Number.MAX_SAFE_INTEGER,
    maxSites: Number.MAX_SAFE_INTEGER,
    leads: true,
    advancedAnalytics: true
};

export const COMMUNITY_ENTITLEMENTS: Entitlements = {
    hasPlans: false,
    plan: signal(null),
    isFree: signal(false),
    features: signal(UNLIMITED_FEATURES),
    planHint: () => ''
};

export const ENTITLEMENTS = new InjectionToken<Entitlements>('ENTITLEMENTS', {
    providedIn: 'root',
    factory: () => COMMUNITY_ENTITLEMENTS
});
