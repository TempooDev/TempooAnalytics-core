import { inject, Injectable, signal, WritableSignal, computed } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { SiteSummary } from './analytics.service';

export interface UserSubscription {
    id: string;
    plan: string;
    features: string; // featuresJson from backend
    startsAt?: string;
    endsAt?: string;
}

export interface UserProfile {
    id: string;
    username: string;
    email?: string;
    isAdmin: boolean;
    subscription?: UserSubscription;
}


export interface AuthResponse {
    accessToken: string;
    expiresIn?: number;
    user?: unknown;
    refreshToken?: string;
}

export interface LoginRequest {
    username: string;
    password: string;
    // Keeps the refresh cookie for 30 days instead of until the browser closes.
    rememberMe?: boolean;
}

// Session tokens live in sessionStorage so they die with the browser session.
// localStorage is only touched to purge tokens persisted by older versions.
const ACCESS_KEY = 'access_token';
const REFRESH_KEY = 'refresh_token';

function readStored(key: string): string | null {
    try {
        return (typeof sessionStorage !== 'undefined') ? sessionStorage.getItem(key) : null;
    } catch {
        return null;
    }
}

function storeTokens(accessToken: string, refreshToken?: string | null): void {
    try {
        sessionStorage.setItem(ACCESS_KEY, accessToken);
        if (refreshToken) {
            sessionStorage.setItem(REFRESH_KEY, refreshToken);
        }
    } catch { }
}

function clearLegacyTokens(): void {
    try {
        if (typeof localStorage !== 'undefined') {
            localStorage.removeItem(ACCESS_KEY);
            localStorage.removeItem(REFRESH_KEY);
        }
    } catch { }
}

function clearStoredTokens(): void {
    try {
        if (typeof sessionStorage !== 'undefined') {
            sessionStorage.removeItem(ACCESS_KEY);
            sessionStorage.removeItem(REFRESH_KEY);
        }
    } catch { }
    clearLegacyTokens();
}

// Drops any token an older version left in localStorage, then reads the current session's token.
function readInitialToken(): string | null {
    clearLegacyTokens();
    return readStored(ACCESS_KEY);
}

@Injectable({ providedIn: 'root' })
export class AuthService {
    private http = inject(HttpClient);
    private api = '/api';

    // 1. Core Authentication State
    private _token: WritableSignal<string | null> = signal<string | null>(
        readInitialToken()
    );

    // 2. Resources using Angular 19+ httpResource
    // Note: We return undefined if there is no token or it is invalid to avoid unnecessary 401 calls on startup
    userResource = httpResource<UserProfile>(() => {
        const token = this._token();
        if (!token || !this.isTokenValid(token)) return undefined;
        return `${this.api}/auth/me`;
    });
    
    // 3. Computed state from DB data (NO FALLBACKS)
    userProfile = computed(() => this.userResource.value() ?? null);


    // Refresh timer reference
    private refreshTimer: any = null;
    // promise used to serialize concurrent refresh attempts
    private refreshPromise: Promise<string | null> | null = null;

    get token(): string | null {
        return this._token();
    }

    tokenSignal() {
        return this._token;
    }

    isAuthenticated(): boolean {
        return !!this._token();
    }

    // Login: backend must set httpOnly refresh cookie when returning access token
    async login(creds: LoginRequest): Promise<AuthResponse> {
        const resp = await this.http.post<AuthResponse>(`${this.api}/auth/login`, creds).toPromise();
        if (resp?.accessToken) {
            this._token.set(resp.accessToken);
            storeTokens(resp.accessToken, resp.refreshToken);
            this.scheduleRefresh(resp.accessToken);

            // Trigger resource reloads
            this.userResource.reload();
        }
        return resp as AuthResponse;
    }

    // Try refresh using httpOnly cookie; returns new access token or null
    async refreshToken(): Promise<string | null> {
        if (this.refreshPromise) return this.refreshPromise;
        this.refreshPromise = (async () => {
            try {
                const stored = readStored(REFRESH_KEY);
                if (stored) {
                    // A revoked or expired stored token answers 401: fall through to the cookie and then clear the session.
                    try {
                        const resp = await this.http.post<AuthResponse>(`${this.api}/auth/refresh`, { refreshToken: stored }).toPromise();
                        if (resp?.accessToken) {
                            this._token.set(resp.accessToken);
                            storeTokens(resp.accessToken, resp.refreshToken);
                            this.scheduleRefresh(resp.accessToken);
                            return resp.accessToken;
                        }
                    } catch { }
                }

                // Fallback: try fetch with credentials to support cookie-based refresh
                try {
                    const refreshUrl = `${this.api}/auth/refresh`;
                    const r = await fetch(refreshUrl, { method: 'POST', credentials: 'include' });
                    if (r && r.ok) {
                        const j = await r.json();
                        if (j?.accessToken) {
                            this._token.set(j.accessToken);
                            storeTokens(j.accessToken, j?.refreshToken);
                            this.scheduleRefresh(j.accessToken);
                            return j.accessToken;
                        }
                    }
                } catch { }
                this._token.set(null);
                clearStoredTokens();
                return null;
            } finally {
                setTimeout(() => (this.refreshPromise = null));
            }
            return null;
        })();

        return this.refreshPromise;
    }

    // Check if token is locally valid (not expired)
    isTokenValid(token: string): boolean {
        try {
            const parts = token.split('.');
            if (parts.length !== 3) return false;
            const payload = JSON.parse(atob(parts[1]));
            const expiresAt = payload.exp * 1000;
            return Date.now() < expiresAt;
        } catch {
            return false;
        }
    }

    // Try to restore session at app startup. Returns true if session restored.
    // Runs in APP_INITIALIZER, where a rejection aborts bootstrap and leaves a blank page, so it never throws.
    async tryRestoreSession(): Promise<boolean> {
        // 1. Check if we already have a valid token in memory/storage
        const currentToken = this._token();
        let restored = false;
        if (currentToken && this.isTokenValid(currentToken)) {
            this.scheduleRefresh(currentToken);
            restored = true;
        } else {
            // 2. If no valid token, try to refresh it
            try {
                restored = !!(await this.refreshToken());
            } catch {
                this.clearLocalSession();
            }
        }

        if (restored) {
            this.userResource.reload();
        }
        return restored;
    }

    private scheduleRefresh(accessToken: string) {
        if (this.refreshTimer) clearTimeout(this.refreshTimer);

        try {
            const payload = JSON.parse(atob(accessToken.split('.')[1]));
            const expiresAt = payload.exp * 1000;
            const now = Date.now();
            // Refresh 1 minute before expiry (or 30s if very short lived)
            const buffer = 60 * 1000;
            const delay = Math.max(0, (expiresAt - now) - buffer);

            this.refreshTimer = setTimeout(() => {
                this.refreshToken();
            }, delay);
        } catch (e) {
            // Failed to schedule refresh
        }
    }

    // Logout: notify backend to revoke refresh token and clear access token
    async logout(): Promise<void> {
        if (this.refreshTimer) clearTimeout(this.refreshTimer);
        try {
            await this.http.post(`${this.api}/auth/logout`, null).toPromise();
        } catch { }
        clearStoredTokens();
        this._token.set(null);
    }

    // Local cleanup only
    clearLocalSession(): void {
        if (this.refreshTimer) clearTimeout(this.refreshTimer);
        clearStoredTokens();
        this._token.set(null);
    }

    // Convenience: development-only token endpoint (keeps backward compat). Backend may return cookie.
    async requestDevToken(userId: string, displayName?: string): Promise<AuthResponse> {
        const body = { userId, displayName };
        const respRaw = await this.http.post<any>(`${this.api}/auth/token`, body).toPromise();
        const resp: AuthResponse = { accessToken: respRaw?.accessToken ?? respRaw?.access_token, expiresIn: respRaw?.expiresIn ?? respRaw?.expires_in, user: respRaw?.user };
        if (resp.accessToken) {
            this._token.set(resp.accessToken);
            storeTokens(resp.accessToken, respRaw?.refreshToken);
            this.scheduleRefresh(resp.accessToken);
            this.userResource.reload();
        }
        return resp;
    }

    async adminSession(password: string): Promise<AuthResponse> {
        const resp = await this.http.post<AuthResponse>(`${this.api}/auth/admin-session`, { password }).toPromise();
        if (resp?.accessToken) {
            this._token.set(resp.accessToken);
            this.userResource.reload();
        }
        return resp as AuthResponse;
    }

    async forgotPassword(email: string): Promise<void> {
        await this.http.post(`${this.api}/auth/forgot-password`, { email }).toPromise();
    }

    async changePassword(req: { oldPassword: string; newPassword: string }): Promise<void> {
        await this.http.post(`${this.api}/auth/change-password`, req).toPromise();
    }

    async getProfile(): Promise<UserProfile> {
        const p = await this.http.get<UserProfile>(`${this.api}/auth/me`).toPromise();
        return p!;
    }

    async listSites(): Promise<SiteSummary[]> {
        const res = await this.http.get<SiteSummary[]>(`${this.api}/analytics/sites`).toPromise();
        return res ?? [];
    }

    async getAllUsers(): Promise<any[]> {
        const res = await this.http.get<any[]>(`${this.api}/auth/users`).toPromise();
        return res ?? [];
    }
}
