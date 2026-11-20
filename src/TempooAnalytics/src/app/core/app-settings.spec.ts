import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { APP_SETTINGS, DEFAULT_BRAND_NAME, apiUrlAttribute, instanceSettings } from './app-settings';

const win = (origin: string, runtime?: { brandName?: string; supportEmail?: string }) =>
    ({ location: { origin } as Location, __TEMPOO_CONFIG__: runtime });

describe('instance settings (what the core uses when nothing overrides them)', () => {
    it('derives the tracker script and the events endpoint from the instance origin', () => {
        const s = instanceSettings(win('https://stats.example.org'));
        expect(s.trackingScriptUrl).toBe('https://stats.example.org/assets/analytics.js');
        expect(s.trackingApiUrl).toBe('https://stats.example.org/analytics/events');
    });

    it('names no hosted service and carries no contact address by default', () => {
        const s = instanceSettings(win('http://localhost:8000'));
        expect(JSON.stringify(s)).not.toMatch(/tempooanalytics\.(eu|com)|@/);
        expect(s.supportEmail).toBe('');
        expect(s.brandName).toBe(DEFAULT_BRAND_NAME);
        expect(s.brandUrl).toBe('');
    });

    it('takes the brand and the support address from the runtime config', () => {
        const s = instanceSettings(win('https://x.test', { brandName: ' Acme Stats ', supportEmail: 'ops@acme.test' }));
        expect(s.brandName).toBe('Acme Stats');
        expect(s.supportEmail).toBe('ops@acme.test');
    });

    it('is the injected default of the app', () => {
        TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
        expect(TestBed.inject(APP_SETTINGS).supportEmail).toBe('');
        expect(TestBed.inject(APP_SETTINGS).trackingScriptUrl).toContain('/assets/analytics.js');
    });

    it('adds data-api-url to the snippet only when there is an API URL', () => {
        expect(apiUrlAttribute({ ...instanceSettings(win('https://x.test')), trackingApiUrl: '' })).toBe('');
        expect(apiUrlAttribute(instanceSettings(win('https://x.test')))).toBe(' data-api-url="https://x.test/analytics/events"');
    });
});
