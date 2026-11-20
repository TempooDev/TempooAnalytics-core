import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import template from './import-management.page.html?raw';
import { IMPORT_POLL_INTERVAL_MS, ImportManagementPage, ga4FailureTitle } from './import-management.page';
import { GA4_SITE_STORAGE_KEY, ImportJobDto, ImportService, ImportStatus, ImportType } from '../../../shared/services/import.service';
import { ToastService } from '../../../shared/services/toast.service';
import { FeatureFlagsService } from '../../../shared/services/feature-flags.service';
import { COMMUNITY_ENTITLEMENTS, ENTITLEMENTS } from '@app/core/entitlements';

const job = (status: ImportStatus, extra: Partial<ImportJobDto> = {}): ImportJobDto => ({
    id: 'job-1', siteId: 'site-1', type: ImportType.Plausible, status, createdAt: '2026-10-01T10:00:00Z', rowsProcessed: 0, ...extra
});

describe('ImportManagementPage', () => {
    let getImports: ReturnType<typeof vi.fn>;
    let authorizeGa4: ReturnType<typeof vi.fn>;
    let toast: { success: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn>; info: ReturnType<typeof vi.fn> };

    function createPage({ ga4 = false, plan = 'pro', storage = true } = {}) {
        toast = { success: vi.fn(), error: vi.fn(), info: vi.fn() };
        TestBed.configureTestingModule({
            providers: [
                provideZonelessChangeDetection(),
                { provide: ActivatedRoute, useValue: { params: of({ siteId: 'site-1' }) } },
                { provide: ImportService, useValue: { getImports, authorizeGa4 } },
                { provide: ToastService, useValue: toast },
                { provide: FeatureFlagsService, useValue: { ga4Import: signal(ga4), plausibleImport: signal(true), dataStorage: signal(storage) } },
                { provide: ENTITLEMENTS, useValue: { ...COMMUNITY_ENTITLEMENTS, isFree: signal(plan === 'free') } }
            ]
        });
        return TestBed.runInInjectionContext(() => new ImportManagementPage());
    }

    beforeEach(() => vi.useFakeTimers());
    afterEach(() => {
        vi.useRealTimers();
        TestBed.resetTestingModule();
    });

    it('refreshes the list every 5 s while a job is pending or processing, then stops', () => {
        getImports = vi.fn()
            .mockReturnValueOnce(of([job(ImportStatus.Pending)]))
            .mockReturnValueOnce(of([job(ImportStatus.Processing)]))
            .mockReturnValue(of([job(ImportStatus.Completed)]));
        const page = createPage();

        page.ngOnInit();
        expect(getImports).toHaveBeenCalledTimes(1);
        expect(page.isImporting()).toBe(true);

        vi.advanceTimersByTime(IMPORT_POLL_INTERVAL_MS);
        expect(getImports).toHaveBeenCalledTimes(2);

        vi.advanceTimersByTime(IMPORT_POLL_INTERVAL_MS);
        expect(getImports).toHaveBeenCalledTimes(3);
        expect(page.isImporting()).toBe(false);

        vi.advanceTimersByTime(IMPORT_POLL_INTERVAL_MS * 3);
        expect(getImports).toHaveBeenCalledTimes(3);
    });

    it('replaces the upload cards with an explanation when the server has no object storage', () => {
        getImports = vi.fn().mockReturnValue(of([]));
        expect(createPage({ storage: true }).storageMissing()).toBe(false);
        TestBed.resetTestingModule();
        expect(createPage({ storage: false }).storageMissing()).toBe(true);

        expect(template).toContain('@if (storageMissing())');
        expect(template).toContain('data-testid="storage-missing"');
        expect(template).toContain('no tiene almacenamiento de objetos S3 configurado');
    });

    it('explains a 501 from the API in Spanish', () => {
        getImports = vi.fn().mockReturnValue(of([]));
        const page = createPage();
        const error = new HttpErrorResponse({ status: 501, error: { error: 'x' } });
        // The API's English message names the server's config; the UI answers in Spanish.
        expect(page.uploadErrorMessage(error)).toContain('no está disponible en este servidor');
    });

    it('does not poll when no job is running', () => {
        getImports = vi.fn().mockReturnValue(of([job(ImportStatus.Failed, { errorMessage: 'missing visitors table' })]));
        const page = createPage();

        page.ngOnInit();
        vi.advanceTimersByTime(IMPORT_POLL_INTERVAL_MS * 2);

        expect(getImports).toHaveBeenCalledTimes(1);
    });

    it('explains upload errors', () => {
        getImports = vi.fn().mockReturnValue(of([]));
        const page = createPage();
        const error = (status: number, body: unknown) => new HttpErrorResponse({ status, error: body });

        expect(page.uploadErrorMessage(error(400, 'Only Plausible export .zip or .csv files are supported')))
            .toBe('Only Plausible export .zip or .csv files are supported');
        expect(page.uploadErrorMessage(error(403, { error: 'Importing history is available on paid plans. Please upgrade your plan.' })))
            .toContain('paid plans');
        expect(page.uploadErrorMessage(error(409, 'An import job is already in progress for this site.')))
            .toContain('Another import is still running');
    });

    it('shows how long a finished job took', () => {
        getImports = vi.fn().mockReturnValue(of([]));
        const page = createPage();

        expect(page.getDuration(job(ImportStatus.Completed, { startedAt: '2026-10-01T10:00:00Z', completedAt: '2026-10-01T10:01:05Z' }))).toBe('1m 05s');
        expect(page.getDuration(job(ImportStatus.Processing, { startedAt: '2026-10-01T10:00:00Z' }))).toBe('—');
    });

    it('accepts the Plausible zip and replaces the GA4 form with a coming-soon card', () => {
        const container = document.createElement('div');
        container.innerHTML = template;

        expect(container.querySelector('input[type="file"]')?.getAttribute('accept')).toBe('.zip,.csv');
        expect(container.textContent).toContain('Imports & Exports');
        expect(container.textContent).toContain('coming soon');
        expect(container.querySelector('input[type="password"]')).toBeNull();
    });

    describe('Google Analytics', () => {
        beforeEach(() => sessionStorage.clear());

        it('stays "coming soon" while the flag is off', () => {
            getImports = vi.fn().mockReturnValue(of([]));
            const page = createPage({ ga4: false });

            expect(page.ga4Enabled()).toBe(false);
            expect(template).toContain('@if (!ga4Enabled())');
        });

        it('offers "Connect with Google" on paid plans and an upgrade on the free plan', () => {
            getImports = vi.fn().mockReturnValue(of([]));
            expect(createPage({ ga4: true, plan: 'pro' }).isPaidPlan()).toBe(true);
            TestBed.resetTestingModule();
            expect(createPage({ ga4: true, plan: 'free' }).isPaidPlan()).toBe(false);

            expect(template).toContain('Connect with Google');
            expect(template).toContain('data-testid="ga4-locked"');
        });

        it('warns that the import replaces the history imported before', () => {
            getImports = vi.fn().mockReturnValue(of([job(ImportStatus.Completed)]));
            const page = createPage({ ga4: true });

            page.ngOnInit();

            expect(page.hasImportedHistory()).toBe(true);
            expect(template).toContain('Importing from Google Analytics replaces it.');
        });

        it('remembers the site and sends the browser to Google', () => {
            getImports = vi.fn().mockReturnValue(of([]));
            authorizeGa4 = vi.fn().mockReturnValue(of({ authorizationUrl: 'https://accounts.google.com/o/oauth2/v2/auth?x=1' }));
            const page = createPage({ ga4: true });
            const redirect = vi.spyOn(page, 'redirectTo').mockImplementation(() => {});
            page.ngOnInit();

            page.connectGoogle();

            expect(authorizeGa4).toHaveBeenCalledWith('site-1');
            expect(sessionStorage.getItem(GA4_SITE_STORAGE_KEY)).toBe('site-1');
            expect(redirect).toHaveBeenCalledWith('https://accounts.google.com/o/oauth2/v2/auth?x=1');
        });

        it('explains why the authorization could not start', () => {
            getImports = vi.fn().mockReturnValue(of([]));
            authorizeGa4 = vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
            const page = createPage({ ga4: true });
            const redirect = vi.spyOn(page, 'redirectTo').mockImplementation(() => {});
            page.ngOnInit();

            page.connectGoogle();

            expect(redirect).not.toHaveBeenCalled();
            expect(toast.error).toHaveBeenCalledWith(expect.stringContaining('Another import is still running'));
            expect(page.connectingGoogle()).toBe(false);
        });

        it('names the reason of failed GA4 jobs and offers to connect again', () => {
            expect(ga4FailureTitle('The Google Analytics quota for this property is exhausted. Try again tomorrow.')).toContain('quota');
            expect(ga4FailureTitle('Google Analytics denied access (it may have been revoked). Connect with Google again.')).toContain('revoked');
            expect(ga4FailureTitle("The Google Analytics property has no data before this site's first Tempoo event, so there was nothing to import.")).toBe('Nothing to import');
            expect(ga4FailureTitle('The download from Google Analytics was interrupted. Connect with Google again to retry.')).toContain('interrupted');

            const failedGa4 = job(ImportStatus.Failed, { type: ImportType.GoogleAnalytics4, errorMessage: 'quota' });
            getImports = vi.fn().mockReturnValue(of([failedGa4]));
            const page = createPage({ ga4: true });
            page.ngOnInit();

            expect(page.canReconnect(failedGa4)).toBe(true);
            expect(page.canReconnect(job(ImportStatus.Failed))).toBe(false); // Plausible
            expect(page.canReconnect(job(ImportStatus.Completed, { type: ImportType.GoogleAnalytics4 }))).toBe(false);
            expect(template).toContain('data-testid="ga4-reconnect"');
        });
    });
});
