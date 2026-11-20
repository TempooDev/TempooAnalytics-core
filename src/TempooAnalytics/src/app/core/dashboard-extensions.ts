import { EnvironmentProviders, InjectionToken, makeEnvironmentProviders } from '@angular/core';
import { ProfileExtension } from './profile-extensions';

/** A block rendered on the site dashboard by a feature package. It receives `siteId` as an input. */
export type DashboardExtension = ProfileExtension;

export const DASHBOARD_EXTENSIONS = new InjectionToken<DashboardExtension[]>('DASHBOARD_EXTENSIONS');

export function provideDashboardExtensions(...extensions: DashboardExtension[]): EnvironmentProviders {
    return makeEnvironmentProviders([{ provide: DASHBOARD_EXTENSIONS, useValue: extensions, multi: true }]);
}
