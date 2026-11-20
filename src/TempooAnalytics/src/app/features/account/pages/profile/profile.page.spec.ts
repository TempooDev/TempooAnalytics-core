import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import template from './profile.page.html?raw';
import { ProfilePage } from './profile.page';
import { AuthService } from '@app/shared/services/auth.service';
import { AnalyticsService } from '@app/shared/services/analytics.service';
import { FeatureFlagsService } from '@app/shared/services/feature-flags.service';
import { COMMUNITY_ENTITLEMENTS, ENTITLEMENTS, Entitlements } from '@app/core/entitlements';
import { PROFILE_EXTENSIONS, ProfileExtension } from '@app/core/profile-extensions';

class A {}
class B {}
class C {}
class D {}

describe('ProfilePage', () => {
    function createPage({ publicApi = false, extensions, entitlements = COMMUNITY_ENTITLEMENTS }: {
        publicApi?: boolean; extensions?: ProfileExtension[][]; entitlements?: Entitlements;
    } = {}) {
        const resource = { value: signal(undefined), isLoading: signal(false) };
        TestBed.configureTestingModule({
            providers: [
                provideZonelessChangeDetection(),
                { provide: AuthService, useValue: { userProfile: signal(null), userResource: resource } },
                { provide: AnalyticsService, useValue: { getDashboard: () => of(undefined) } },
                { provide: FeatureFlagsService, useValue: { isOn: (name: string) => name === 'publicApi' && publicApi } },
                { provide: ENTITLEMENTS, useValue: entitlements },
                ...(extensions ?? []).map(e => ({ provide: PROFILE_EXTENSIONS, useValue: e, multi: true }))
            ]
        });
        const page = TestBed.runInInjectionContext(() => new ProfilePage());
        TestBed.tick();
        return page;
    }

    afterEach(() => TestBed.resetTestingModule());

    describe('extensions', () => {
        it('is empty when no package registers any', () => {
            const page = createPage();
            expect(page.extensions()).toEqual([]);
            expect(page.topExtensions()).toEqual([]);
            expect(page.bottomExtensions()).toEqual([]);
        });

        it('merges every registration, sorted by order, and applies visibility', () => {
            const page = createPage({
                extensions: [
                    [{ id: 'b', order: 20, component: B }, { id: 'hidden', component: C, visible: f => f.isOn('publicApi') }],
                    [{ id: 'a', order: 5, component: A }]
                ]
            });
            expect(page.extensions().map(e => e.id)).toEqual(['a', 'b']);
        });

        it('shows a flag-gated block once its flag is on', () => {
            const page = createPage({ publicApi: true, extensions: [[{ id: 'k', component: A, visible: f => f.isOn('publicApi') }]] });
            expect(page.extensions().map(e => e.id)).toEqual(['k']);
        });

        it('splits blocks into above and below the usage statistics', () => {
            const page = createPage({
                extensions: [[
                    { id: 'sub', order: 10, placement: 'top', component: A },
                    { id: 'credits', order: 20, component: B },
                    { id: 'api', order: 30, placement: 'bottom', component: C },
                    { id: 'late-top', order: 50, placement: 'top', component: D }
                ]]
            });
            expect(page.topExtensions().map(e => e.id)).toEqual(['sub', 'late-top']);
            expect(page.bottomExtensions().map(e => e.id)).toEqual(['credits', 'api']);
        });

        it('renders both slots through ngComponentOutlet', () => {
            expect(template).toContain('@for (ext of topExtensions(); track ext.id)');
            expect(template).toContain('@for (ext of bottomExtensions(); track ext.id)');
            expect(template).toContain('ngComponentOutlet');
        });
    });

    describe('usage quota', () => {
        it('has no quota in a build without plans', () => {
            const page = createPage();
            expect(page.hasQuota()).toBe(false);
        });

        it('shows the quota and the percentage for a plan with a limit', () => {
            const page = createPage({
                entitlements: { ...COMMUNITY_ENTITLEMENTS, features: signal({ maxMonthlyEvents: 10000, maxSites: 1, leads: false, advancedAnalytics: false }) }
            });
            page.events30.set(2500);
            expect(page.hasQuota()).toBe(true);
            expect(page.usagePercentage()).toBe(25);
            page.events30.set(99999);
            expect(page.usagePercentage()).toBe(100);
        });
    });

    it('does not know any billing or AI concept (they live in extensions)', () => {
        expect(template).not.toMatch(/subscription|credits|weeklyDigest/i);
    });
});
