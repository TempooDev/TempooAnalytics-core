import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { AuthService } from '@app/shared/services/auth.service';
import { COMMUNITY_ENTITLEMENTS, ENTITLEMENTS } from '@app/core/entitlements';
import { LoginPage } from './login.page';
import loginTemplate from './login.page.html?raw';

describe('LoginPage (template smoke)', () => {
    it('renders input elements present in template', () => {
        const container = document.createElement('div');
        container.innerHTML = loginTemplate;
        expect(container.querySelectorAll('input').length).toBeGreaterThan(0);
    });

    it('has an unchecked "remember me" checkbox with a label', () => {
        const container = document.createElement('div');
        container.innerHTML = loginTemplate;
        const box = container.querySelector<HTMLInputElement>('input#rememberMe');
        expect(box?.type).toBe('checkbox');
        expect(box?.hasAttribute('checked')).toBe(false);
        expect(container.querySelector('label[for="rememberMe"]')?.textContent).toContain('Remember me (30 days)');
    });
});

describe('LoginPage sign-up hint', () => {
    afterEach(() => TestBed.resetTestingModule());

    function selfHostFlag(hasPlans: boolean): boolean {
        TestBed.configureTestingModule({
            providers: [
                provideZonelessChangeDetection(),
                { provide: AuthService, useValue: {} },
                { provide: Router, useValue: {} },
                { provide: ENTITLEMENTS, useValue: { ...COMMUNITY_ENTITLEMENTS, hasPlans } },
            ],
        });
        return TestBed.runInInjectionContext(() => new LoginPage()).selfHost;
    }

    it('explains how users are created when the build has no plans', () => {
        expect(selfHostFlag(false)).toBe(true);
        expect(loginTemplate).toContain('data-testid="login-no-signup"');
        expect(loginTemplate).toContain('/admin/users');
    });

    it('does not show it when the build sells plans', () => {
        expect(selfHostFlag(true)).toBe(false);
    });
});

describe('LoginPage.submit', () => {
    const login = vi.fn().mockResolvedValue({});

    beforeEach(() => {
        login.mockClear();
        TestBed.configureTestingModule({
            providers: [
                provideZonelessChangeDetection(),
                { provide: AuthService, useValue: { login } },
                { provide: Router, useValue: { navigateByUrl: vi.fn().mockResolvedValue(true) } },
            ],
        });
    });

    afterEach(() => TestBed.resetTestingModule());

    it.each([false, true])('sends rememberMe=%s', async (rememberMe) => {
        const page = TestBed.runInInjectionContext(() => new LoginPage());
        page.username.set('user');
        page.password.set('pass');
        if (rememberMe) page.rememberMe.set(true);

        await page.submit();

        expect(login).toHaveBeenCalledWith({ username: 'user', password: 'pass', rememberMe });
    });
});
