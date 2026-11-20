import { EnvironmentProviders, InjectionToken, inject, makeEnvironmentProviders } from '@angular/core';
import { Route, Routes } from '@angular/router';

/**
 * Routes contributed by optional feature packages (e.g. the private cloud build).
 * They are mounted inside the authenticated layout, after the core routes.
 */
export const FEATURE_ROUTES = new InjectionToken<Routes[]>('FEATURE_ROUTES');

/**
 * Routes contributed by feature packages that live outside the authenticated layout
 * (for example a public checkout confirmation page).
 */
export const PUBLIC_FEATURE_ROUTES = new InjectionToken<Routes[]>('PUBLIC_FEATURE_ROUTES');

/** Registers extra routes for the authenticated layout. Can be called several times. */
export function provideFeatureRoutes(routes: Routes): EnvironmentProviders {
    return makeEnvironmentProviders([{ provide: FEATURE_ROUTES, useValue: routes, multi: true }]);
}

/** Registers extra routes outside the authenticated layout. Can be called several times. */
export function providePublicFeatureRoutes(routes: Routes): EnvironmentProviders {
    return makeEnvironmentProviders([{ provide: PUBLIC_FEATURE_ROUTES, useValue: routes, multi: true }]);
}

/**
 * Empty-path route that lazily mounts everything registered via FEATURE_ROUTES.
 * Put it last among the layout's children; with nothing registered it matches nothing.
 */
export const featureRoutesFallback: Route = {
    path: '',
    loadChildren: () => (inject(FEATURE_ROUTES, { optional: true }) ?? []).flat()
};

/** Same as {@link featureRoutesFallback}, for the root level (outside the layout). Put it last in the root routes. */
export const publicFeatureRoutesFallback: Route = {
    path: '',
    loadChildren: () => (inject(PUBLIC_FEATURE_ROUTES, { optional: true }) ?? []).flat()
};
