import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { firstValueFrom } from 'rxjs';
import { HeatmapService } from './heatmap.service';

describe('HeatmapService', () => {
    let http: HttpTestingController;
    let service: HeatmapService;

    beforeEach(() => {
        TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()] });
        http = TestBed.inject(HttpTestingController);
        service = TestBed.inject(HeatmapService);
    });

    afterEach(() => {
        http.verify();
        TestBed.resetTestingModule();
    });

    it('lists pages for the site, device and range', async () => {
        const pages = firstValueFrom(service.getPages({ siteId: 'site-1', device: 'mobile', days: 7 }));

        const req = http.expectOne(r => r.url === '/api/analytics/heatmaps/pages');
        expect(req.request.params.get('siteId')).toBe('site-1');
        expect(req.request.params.get('device')).toBe('mobile');
        const from = new Date(req.request.params.get('from')!);
        const to = new Date(req.request.params.get('to')!);
        expect((to.getTime() - from.getTime()) / 86_400_000).toBe(7);
        req.flush([{ path: '/precios', clicks: 12, rageClicks: 2, deadClicks: 1 }]);

        expect((await pages)[0].path).toBe('/precios');
    });

    it('asks for the heatmap of one path', async () => {
        const map = firstValueFrom(service.getHeatmap({ siteId: 'site-1', device: 'desktop', days: 30 }, '/precios'));

        const req = http.expectOne(r => r.url === '/api/analytics/heatmaps');
        expect(req.request.params.get('path')).toBe('/precios');
        req.flush({ path: '/precios', device: 'desktop', totalClicks: 3, points: [], topElements: [], scroll: [] });

        expect((await map).totalClicks).toBe(3);
    });

    it('requests an overlay token with the site and path', async () => {
        const token = firstValueFrom(service.createOverlayToken('site-1', '/precios'));

        const req = http.expectOne('/api/analytics/heatmaps/overlay-token');
        expect(req.request.method).toBe('POST');
        expect(req.request.body).toEqual({ siteId: 'site-1', path: '/precios' });
        req.flush({ token: 'abc', expiresAt: '2026-10-23T10:10:00Z', siteUrl: 'https://tienda.es' });

        expect((await token).siteUrl).toBe('https://tienda.es');
    });

    it('puts the token in the hash of the customer page', () => {
        expect(service.overlayUrl('https://tienda.es/', '/precios', 'a+b/c')).toBe('https://tienda.es/precios#tempoo-heatmap=a%2Bb%2Fc');
    });
});
