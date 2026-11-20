import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Location } from '@angular/common';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import template from './ga4-callback.page.html?raw';
import { Ga4CallbackPage } from './ga4-callback.page';
import { GA4_SITE_STORAGE_KEY, ImportService } from '../../../shared/services/import.service';
import { ToastService } from '../../../shared/services/toast.service';

const properties = [
    { id: 'properties/123', name: 'acme.com', account: 'Acme' },
    { id: 'properties/456', name: 'blog.acme.com', account: 'Acme' }
];

describe('Ga4CallbackPage', () => {
    let importService: { connectGa4: ReturnType<typeof vi.fn>; startGa4Import: ReturnType<typeof vi.fn>; authorizeGa4: ReturnType<typeof vi.fn> };
    let router: { navigate: ReturnType<typeof vi.fn> };
    let location: { path: ReturnType<typeof vi.fn>; replaceState: ReturnType<typeof vi.fn> };
    let toast: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };

    function createPage(query: Record<string, string>) {
        TestBed.configureTestingModule({
            providers: [
                provideZonelessChangeDetection(),
                { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } },
                { provide: ImportService, useValue: importService },
                { provide: Router, useValue: router },
                { provide: Location, useValue: location },
                { provide: ToastService, useValue: toast }
            ]
        });
        const page = TestBed.runInInjectionContext(() => new Ga4CallbackPage());
        page.ngOnInit();
        return page;
    }

    beforeEach(() => {
        sessionStorage.clear();
        sessionStorage.setItem(GA4_SITE_STORAGE_KEY, 'site-1');
        importService = {
            connectGa4: vi.fn().mockReturnValue(of({ connectionId: 'conn-1', properties })),
            startGa4Import: vi.fn().mockReturnValue(of({ jobId: 'job-1' })),
            authorizeGa4: vi.fn().mockReturnValue(of({ authorizationUrl: 'https://accounts.google.com/o/oauth2/v2/auth?x=1' }))
        };
        router = { navigate: vi.fn() };
        location = { path: vi.fn().mockReturnValue('/account/imports/google/callback?code=c&state=s'), replaceState: vi.fn() };
        toast = { success: vi.fn(), error: vi.fn() };
    });

    afterEach(() => TestBed.resetTestingModule());

    it('connects, lists the properties and starts the import of the chosen one', () => {
        const page = createPage({ code: 'c', state: 's' });

        expect(importService.connectGa4).toHaveBeenCalledWith('site-1', 'c', 's');
        expect(location.replaceState).toHaveBeenCalledWith('/account/imports/google/callback');
        expect(page.step()).toBe('choose');
        expect(page.properties()).toEqual(properties);

        page.selectedPropertyId.set('properties/456');
        page.startImport();

        expect(importService.startGa4Import).toHaveBeenCalledWith('site-1', 'conn-1', 'properties/456');
        expect(router.navigate).toHaveBeenCalledWith(['/account/sites', 'site-1', 'imports']);
        expect(sessionStorage.getItem(GA4_SITE_STORAGE_KEY)).toBeNull();
        expect(template).toContain('data-testid="ga4-properties"');
    });

    it('says the connection was cancelled when the user denies access', () => {
        const page = createPage({ error: 'access_denied' });

        expect(page.error()).toBe('cancelled');
        expect(importService.connectGa4).not.toHaveBeenCalled();
        expect(template).toContain('The connection with Google was cancelled.');
    });

    it('says no GA4 property was found', () => {
        importService.connectGa4.mockReturnValue(of({ connectionId: '', properties: [] }));

        const page = createPage({ code: 'c', state: 's' });

        expect(page.error()).toBe('noProperties');
        expect(template).toContain('No Google Analytics 4 property was found.');
    });

    it('asks to connect again when the authorization expired', () => {
        importService.connectGa4.mockReturnValue(throwError(() => new HttpErrorResponse({
            status: 400, error: { error: 'The Google authorization expired or is not valid. Connect with Google again.' }
        })));
        const page = createPage({ code: 'c', state: 's' });
        const redirect = vi.spyOn(page, 'redirectTo').mockImplementation(() => {});

        expect(page.error()).toBe('expired');
        expect(page.errorDetail()).toContain('Connect with Google again');

        page.reconnect();

        expect(importService.authorizeGa4).toHaveBeenCalledWith('site-1');
        expect(redirect).toHaveBeenCalledWith('https://accounts.google.com/o/oauth2/v2/auth?x=1');
    });

    it('treats a missing site or code as an expired authorization', () => {
        sessionStorage.clear();

        const page = createPage({ code: 'c', state: 's' });

        expect(page.error()).toBe('expired');
        expect(importService.connectGa4).not.toHaveBeenCalled();
    });

    it('explains a 409 when another import started meanwhile', () => {
        importService.startGa4Import.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
        const page = createPage({ code: 'c', state: 's' });

        page.startImport();

        expect(page.error()).toBe('conflict');
        expect(router.navigate).not.toHaveBeenCalled();
        expect(template).toContain('Another import is still running for this site.');
    });

    it('asks to connect again when the property choice comes too late', () => {
        importService.startGa4Import.mockReturnValue(throwError(() => new HttpErrorResponse({
            status: 400, error: { error: 'The Google authorization expired or is not valid. Connect with Google again.' }
        })));
        const page = createPage({ code: 'c', state: 's' });

        page.startImport();

        expect(page.error()).toBe('expired');
    });
});
