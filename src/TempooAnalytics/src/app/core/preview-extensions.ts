import { EnvironmentProviders, InjectionToken, makeEnvironmentProviders } from '@angular/core';
import { ProfileExtension } from './profile-extensions';

/**
 * A block rendered in the header of the public preview page by a feature package (e.g. a call to action).
 * It receives the preview `token` as an input.
 */
export type PreviewExtension = ProfileExtension;

export const PREVIEW_EXTENSIONS = new InjectionToken<PreviewExtension[]>('PREVIEW_EXTENSIONS');

export function providePreviewExtensions(...extensions: PreviewExtension[]): EnvironmentProviders {
    return makeEnvironmentProviders([{ provide: PREVIEW_EXTENSIONS, useValue: extensions, multi: true }]);
}
