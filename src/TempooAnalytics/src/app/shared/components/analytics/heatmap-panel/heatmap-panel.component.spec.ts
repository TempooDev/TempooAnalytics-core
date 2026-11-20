import { Component, Input, provideZonelessChangeDetection, ɵresolveComponentResources as resolveComponentResources } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import template from './heatmap-panel.component.html?raw';
import { vi } from 'vitest';
import { HeatmapPanelComponent } from './heatmap-panel.component';
import { ToastService } from '../../../services/toast.service';
import { UPSELL_COMPONENT, UpsellKind } from '@app/core/upsell';

@Component({ selector: 'test-upsell', standalone: true, template: 'Unlock {{ kind }}' })
class FakeUpsellComponent { @Input() kind: UpsellKind | '' = ''; }

const flushMicrotasks = () => new Promise(r => setTimeout(r));

describe('HeatmapPanelComponent', () => {
    let http: HttpTestingController;

    beforeEach(async () => {
        // Vitest has no Angular compiler plugin, so templateUrl is resolved with the raw template.
        await resolveComponentResources(() => Promise.resolve(template));
        TestBed.configureTestingModule({
            imports: [HeatmapPanelComponent],
            providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
                { provide: ToastService, useValue: { error: vi.fn() } },
                { provide: UPSELL_COMPONENT, useValue: FakeUpsellComponent }]
        });
        http = TestBed.inject(HttpTestingController);
    });

    afterEach(() => TestBed.resetTestingModule());

    function create(locked: boolean) {
        const fixture = TestBed.createComponent(HeatmapPanelComponent);
        fixture.componentRef.setInput('siteId', 'site-1');
        fixture.componentRef.setInput('locked', locked);
        fixture.detectChanges();
        return fixture;
    }

    it('loads the pages and the heatmap of the first one', async () => {
        const fixture = create(false);

        http.expectOne(r => r.url === '/api/analytics/heatmaps/pages')
            .flush([{ path: '/precios', clicks: 12, rageClicks: 2, deadClicks: 1 }]);
        await flushMicrotasks();
        http.expectOne(r => r.url === '/api/analytics/heatmaps' && r.params.get('path') === '/precios').flush({
            path: '/precios', device: 'desktop', totalClicks: 12, points: [],
            topElements: [{ selector: 'a#buy', clicks: 10, rageClicks: 2, deadClicks: 0 }],
            scroll: [{ depth: 10, reachedPct: 90 }]
        });
        await flushMicrotasks();
        fixture.detectChanges();

        const text = fixture.nativeElement.textContent as string;
        expect(text).toContain('/precios');
        expect(text).toContain('a#buy');
        expect(text).toContain('2 rage');
        expect(fixture.componentInstance.frustratedElements()).toHaveLength(1);
    });

    it('shows the upgrade notice and calls nothing without advanced analytics', async () => {
        const fixture = create(true);
        await flushMicrotasks();

        http.expectNone(r => r.url.startsWith('/api/analytics/heatmaps'));
        expect(fixture.nativeElement.textContent).toContain('Unlock heatmaps');
    });
});
