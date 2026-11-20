import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { FeatureFlagsService } from '../services/feature-flags.service';

/** Keeps users out of the Plausible import page until the feature is released. */
export const plausibleImportGuard: CanActivateFn = async () => {
    const flags = inject(FeatureFlagsService);
    const router = inject(Router);
    await flags.load();
    return flags.plausibleImport() || router.parseUrl('/account/sites');
};

/** Keeps users out of the Google Analytics callback until the GA4 import is released. */
export const ga4ImportGuard: CanActivateFn = async () => {
    const flags = inject(FeatureFlagsService);
    const router = inject(Router);
    await flags.load();
    return flags.ga4Import() || router.parseUrl('/account/sites');
};
