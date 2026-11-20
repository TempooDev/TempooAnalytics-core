import { Component, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { featureRoutesFallback, provideFeatureRoutes, providePublicFeatureRoutes, publicFeatureRoutesFallback } from './feature-routes';

@Component({ template: 'billing page', standalone: true })
class BillingPage {}

@Component({ template: 'core page', standalone: true })
class CorePage {}

describe('FEATURE_ROUTES', () => {
    afterEach(() => TestBed.resetTestingModule());

    const coreRoutes = [{ path: 'core', component: CorePage }, featureRoutesFallback];

    function setup(extra: boolean) {
        TestBed.configureTestingModule({
            providers: [
                provideZonelessChangeDetection(),
                provideRouter([{ path: '', children: coreRoutes }]),
                ...(extra ? [provideFeatureRoutes([{ path: 'billing', component: BillingPage }])] : [])
            ]
        });
    }

    it('serves routes registered by a feature package', async () => {
        setup(true);
        const harness = await RouterTestingHarness.create();
        await harness.navigateByUrl('/billing');
        expect(harness.routeNativeElement?.textContent).toContain('billing page');
    });

    it('keeps core routes working next to the fallback', async () => {
        setup(true);
        const harness = await RouterTestingHarness.create();
        await harness.navigateByUrl('/core');
        expect(harness.routeNativeElement?.textContent).toContain('core page');
    });

    it('does not match anything extra when nothing is registered', async () => {
        setup(false);
        const harness = await RouterTestingHarness.create();
        await expect(harness.navigateByUrl('/billing')).rejects.toThrow();
        expect(TestBed.inject(Router).url).not.toBe('/billing');
    });

    it('serves root-level routes registered with providePublicFeatureRoutes', async () => {
        TestBed.configureTestingModule({
            providers: [
                provideZonelessChangeDetection(),
                provideRouter([{ path: 'core', component: CorePage }, publicFeatureRoutesFallback]),
                providePublicFeatureRoutes([{ path: 'checkout/done', component: BillingPage }])
            ]
        });
        const harness = await RouterTestingHarness.create();
        await harness.navigateByUrl('/checkout/done');
        expect(harness.routeNativeElement?.textContent).toContain('billing page');
    });
});
