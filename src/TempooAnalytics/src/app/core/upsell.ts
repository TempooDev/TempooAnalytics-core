import { InjectionToken, Type } from '@angular/core';

/** The places where a build that sells plans shows an upgrade or trial prompt. */
export type UpsellKind =
    | 'header-banner'
    | 'advanced-filters'
    | 'conversion-insights'
    | 'leads-unlock'
    | 'lead-enrichment'
    | 'custom-events'
    | 'funnels'
    | 'heatmaps'
    | 'import-history'
    | 'sites-limit'
    | 'usage-note';

/**
 * Component that renders the upgrade/trial prompt for an `UpsellKind` (it receives the kind as its `kind` input).
 * The core has none (community: no plans, nothing to sell), so none of the wording ships in that build; the cloud build provides it.
 */
export const UPSELL_COMPONENT = new InjectionToken<Type<unknown> | null>('UPSELL_COMPONENT', {
    providedIn: 'root',
    factory: () => null
});
