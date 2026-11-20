import { FeatureFlagsService } from '@app/shared/services/feature-flags.service';
import { EnvironmentProviders, InjectionToken, Type, makeEnvironmentProviders } from '@angular/core';

/** A block rendered on the profile page by a feature package. */
export interface ProfileExtension {
    id: string;
    /** Lower values render first. Defaults to 100. */
    order?: number;
    component: Type<unknown>;
    /** Reactive visibility rule; read the flag signals inside it. Defaults to always visible. */
    visible?: (flags: FeatureFlagsService) => boolean;
    /** 'top' renders above the usage statistics; default is after them. */
    placement?: 'top' | 'bottom';
    /** CSS classes for the wrapper element (default 'contents', so the block's own grid items take part in the layout) (e.g. grid span). */
    hostClass?: string;
}

export const PROFILE_EXTENSIONS = new InjectionToken<ProfileExtension[]>('PROFILE_EXTENSIONS');

export function provideProfileExtensions(...extensions: ProfileExtension[]): EnvironmentProviders {
    return makeEnvironmentProviders([{ provide: PROFILE_EXTENSIONS, useValue: extensions, multi: true }]);
}
