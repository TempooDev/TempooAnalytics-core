import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService } from './auth.service';

function jwt(expiresInSeconds: number): string {
    const payload = btoa(JSON.stringify({ sub: 'user-1', exp: Math.floor(Date.now() / 1000) + expiresInSeconds }));
    return `header.${payload}.signature`;
}

describe('AuthService.tryRestoreSession', () => {
    let http: HttpTestingController;
    let fetchMock: ReturnType<typeof vi.fn>;

    beforeEach(() => {
        sessionStorage.clear();
        // Cookie fallback: no valid refresh cookie either.
        fetchMock = vi.fn().mockResolvedValue({ ok: false });
        vi.stubGlobal('fetch', fetchMock);
        TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()] });
        http = TestBed.inject(HttpTestingController);
    });

    afterEach(() => {
        TestBed.resetTestingModule();
        vi.unstubAllGlobals();
        sessionStorage.clear();
    });

    it('resolves false and clears the session when the stored refresh token is rejected', async () => {
        // Expired access token plus a refresh token the API no longer accepts (e.g. after an API restart).
        sessionStorage.setItem('access_token', jwt(-60));
        sessionStorage.setItem('refresh_token', 'revoked');
        const auth = TestBed.inject(AuthService);

        const restoring = auth.tryRestoreSession();
        http.expectOne('/api/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });

        await expect(restoring).resolves.toBe(false);
        expect(fetchMock).toHaveBeenCalledOnce();
        expect(auth.isAuthenticated()).toBe(false);
        expect(sessionStorage.getItem('access_token')).toBeNull();
        expect(sessionStorage.getItem('refresh_token')).toBeNull();
    });

    it('resolves false when the cookie fallback cannot reach the API', async () => {
        sessionStorage.setItem('refresh_token', 'revoked');
        fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
        const auth = TestBed.inject(AuthService);

        const restoring = auth.tryRestoreSession();
        http.expectOne('/api/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });

        await expect(restoring).resolves.toBe(false);
        expect(sessionStorage.getItem('refresh_token')).toBeNull();
    });

    it('restores the session with a new token when the stored refresh token is accepted', async () => {
        sessionStorage.setItem('refresh_token', 'valid');
        const auth = TestBed.inject(AuthService);
        const fresh = jwt(3600);

        const restoring = auth.tryRestoreSession();
        http.expectOne('/api/auth/refresh').flush({ accessToken: fresh, refreshToken: 'rotated', expiresIn: 3600 });

        await expect(restoring).resolves.toBe(true);
        expect(auth.token).toBe(fresh);
        expect(sessionStorage.getItem('refresh_token')).toBe('rotated');
        expect(fetchMock).not.toHaveBeenCalled();
    });
});

describe('AuthService remember me', () => {
    let http: HttpTestingController;

    beforeEach(() => {
        sessionStorage.clear();
        localStorage.clear();
        TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection(), provideHttpClient(), provideHttpClientTesting()] });
        http = TestBed.inject(HttpTestingController);
    });

    afterEach(() => {
        vi.unstubAllGlobals();
        TestBed.resetTestingModule();
        sessionStorage.clear();
    });

    it.each([true, false])('sends rememberMe=%s with the login request', async (rememberMe) => {
        const auth = TestBed.inject(AuthService);

        const login = auth.login({ username: 'user', password: 'pass', rememberMe });
        const req = http.expectOne('/api/auth/login');
        req.flush({ accessToken: jwt(3600) });
        await login;

        expect(req.request.body).toEqual({ username: 'user', password: 'pass', rememberMe });
    });

    it('restores a remembered session from the refresh cookie', async () => {
        const token = jwt(3600);
        const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ accessToken: token }) });
        vi.stubGlobal('fetch', fetchMock);
        const auth = TestBed.inject(AuthService);

        const restored = await auth.tryRestoreSession();

        expect(restored).toBe(true);
        expect(fetchMock).toHaveBeenCalledWith('/api/auth/refresh', { method: 'POST', credentials: 'include' });
        expect(sessionStorage.getItem('access_token')).toBe(token);
        expect(localStorage.getItem('access_token')).toBeNull();
        expect(localStorage.getItem('refresh_token')).toBeNull();
    });

    it('reports no session and clears tokens when the cookie refresh is rejected', async () => {
        vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false, status: 401 }));
        const auth = TestBed.inject(AuthService);

        expect(await auth.tryRestoreSession()).toBe(false);
        expect(auth.isAuthenticated()).toBe(false);
        expect(sessionStorage.getItem('access_token')).toBeNull();
    });
});
