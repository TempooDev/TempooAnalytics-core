import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { FeatureFlagsService } from './feature-flags.service';

describe('FeatureFlagsService', () => {
    let http: HttpTestingController;

    beforeEach(() => {
        TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()] });
        http = TestBed.inject(HttpTestingController);
    });

    afterEach(() => TestBed.resetTestingModule());

    it('is off until the API enables it', async () => {
        const flags = TestBed.inject(FeatureFlagsService);
        expect(flags.plausibleImport()).toBe(false);

        http.expectOne('/api/features').flush({ plausibleImport: true });
        await flags.load();

        expect(flags.plausibleImport()).toBe(true);
    });

    it('reads the Google Analytics import flag', async () => {
        const flags = TestBed.inject(FeatureFlagsService);
        expect(flags.ga4Import()).toBe(false);

        http.expectOne('/api/features').flush({ plausibleImport: true, ga4Import: true });
        await flags.load();

        expect(flags.ga4Import()).toBe(true);
    });

    it('keeps Google Analytics off when the API does not report it', async () => {
        const flags = TestBed.inject(FeatureFlagsService);

        http.expectOne('/api/features').flush({ plausibleImport: true, ga4Import: false });
        await flags.load();

        expect(flags.ga4Import()).toBe(false);
    });

    it('reads the notifications flag', async () => {
        const flags = TestBed.inject(FeatureFlagsService);
        expect(flags.notifications()).toBe(false);

        http.expectOne('/api/features').flush({ plausibleImport: true, ga4Import: false, notifications: true });
        await flags.load();

        expect(flags.notifications()).toBe(true);
    });

    it('reads the AI weekly digest flag', async () => {
        const flags = TestBed.inject(FeatureFlagsService);
        expect(flags.isOn('aiWeeklyDigest')).toBe(false);

        http.expectOne('/api/features').flush({ plausibleImport: true, ga4Import: false, aiWeeklyDigest: true });
        await flags.load();

        expect(flags.isOn('aiWeeklyDigest')).toBe(true);
    });

    it('reads the AI credits flag', async () => {
        const flags = TestBed.inject(FeatureFlagsService);
        expect(flags.isOn('aiCredits')).toBe(false);

        http.expectOne('/api/features').flush({ plausibleImport: true, ga4Import: false, aiWeeklyDigest: false, aiCredits: true });
        await flags.load();

        expect(flags.isOn('aiCredits')).toBe(true);
    });

    it('reads the heatmaps flag, off unless the API reports it', async () => {
        const flags = TestBed.inject(FeatureFlagsService);
        expect(flags.heatmaps()).toBe(false);

        http.expectOne('/api/features').flush({ plausibleImport: true, ga4Import: false, heatmaps: true });
        await flags.load();

        expect(flags.heatmaps()).toBe(true);
    });

    it('reads the public API flag, off unless the API reports it', async () => {
        const flags = TestBed.inject(FeatureFlagsService);
        expect(flags.isOn('publicApi')).toBe(false);

        http.expectOne('/api/features').flush({ plausibleImport: true, publicApi: true });
        await flags.load();

        expect(flags.isOn('publicApi')).toBe(true);
    });

    it('stays off when the request fails', async () => {
        const flags = TestBed.inject(FeatureFlagsService);

        http.expectOne('/api/features').flush('boom', { status: 500, statusText: 'Server Error' });
        await flags.load();

        expect(flags.plausibleImport()).toBe(false);
        expect(flags.ga4Import()).toBe(false);
        expect(flags.notifications()).toBe(false);
        expect(flags.isOn('aiWeeklyDigest')).toBe(false);
        expect(flags.isOn('aiCredits')).toBe(false);
        expect(flags.heatmaps()).toBe(false);
    });

    it('has object storage unless the API says it is unavailable', async () => {
        const flags = TestBed.inject(FeatureFlagsService);
        expect(flags.dataStorage()).toBe(true);

        http.expectOne('/api/features').flush({ plausibleImport: true, ga4Import: false, dataStorageUnavailable: true });
        await flags.load();

        expect(flags.dataStorage()).toBe(false);
    });

    it('keeps storage available when the API does not mention it (hosted service, or a server with storage)', async () => {
        const flags = TestBed.inject(FeatureFlagsService);
        http.expectOne('/api/features').flush({ plausibleImport: true, ga4Import: true });
        await flags.load();

        expect(flags.dataStorage()).toBe(true);
    });

    it('only asks the API once', async () => {
        const flags = TestBed.inject(FeatureFlagsService);
        http.expectOne('/api/features').flush({ plausibleImport: false });

        await flags.load();
        await flags.load();

        http.expectNone('/api/features');
    });
});
