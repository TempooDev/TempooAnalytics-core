import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, UrlTree } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { guestGuard } from './guest.guard';

function jwt(expiresInSeconds: number): string {
    const payload = btoa(JSON.stringify({ sub: 'u', exp: Math.floor(Date.now() / 1000) + expiresInSeconds }));
    return `header.${payload}.signature`;
}

const run = (guard: typeof guestGuard) =>
    TestBed.runInInjectionContext(() => guard({} as never, {} as never)) as boolean | UrlTree;

describe('guestGuard (login with an active session)', () => {
    function setup(token: string | null) {
        // The real token check, without the HTTP client around it.
        const auth = { token, isTokenValid: (t: string) => AuthService.prototype.isTokenValid.call({}, t) };
        TestBed.configureTestingModule({
            providers: [provideZonelessChangeDetection(), provideRouter([]), { provide: AuthService, useValue: auth }],
        });
    }

    afterEach(() => TestBed.resetTestingModule());

    it('lets a visitor without a session see the login', () => {
        setup(null);
        expect(run(guestGuard)).toBe(true);
    });

    it('lets a visitor with an expired token see the login', () => {
        setup(jwt(-60));
        expect(run(guestGuard)).toBe(true);
    });

    it('sends a visitor with a valid session to the dashboard', () => {
        setup(jwt(3600));
        const result = run(guestGuard);
        expect(result instanceof UrlTree).toBe(true);
        expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/');
    });
});
