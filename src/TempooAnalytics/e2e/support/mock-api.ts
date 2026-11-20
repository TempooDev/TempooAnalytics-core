import { Page } from '@playwright/test';

export const PLAN_SLUGS = ['free'] as const;
export type PlanSlug = (typeof PLAN_SLUGS)[number];

/** The mock API has no plans to offer. */
export const TIERS: object[] = [];

const b64 = (o: object) => Buffer.from(JSON.stringify(o)).toString('base64url');

/** An unsigned JWT that the SPA considers valid for a day (it only reads `exp`). */
export function fakeAccessToken(): string {
    return `${b64({ alg: 'none', typ: 'JWT' })}.${b64({ sub: 'e2e-user', exp: Math.floor(Date.now() / 1000) + 86400 })}.sig`;
}

/** Starts the page with a session, as if the user had logged in earlier. */
export async function signIn(page: Page): Promise<void> {
    await page.addInitScript(t => sessionStorage.setItem('access_token', t), fakeAccessToken());
}

export interface MockOptions {
    plan?: PlanSlug;
    /**
     * Turns on a working login: POST /api/auth/login accepts only these credentials (401 otherwise) and hands out an
     * access token plus a refresh token; POST /api/auth/refresh accepts only that refresh token; logout revokes it.
     */
    credentials?: { username: string; password: string };
}

export const E2E_CREDENTIALS = { username: 'owner', password: 'correct-horse-battery' };
const REFRESH_TOKEN = 'e2e-refresh-token';

/** An access token the SPA considers expired (it only reads `exp`). */
export function expiredAccessToken(): string {
    return `${b64({ alg: 'none', typ: 'JWT' })}.${b64({ sub: 'e2e-user', exp: Math.floor(Date.now() / 1000) - 60 })}.sig`;
}

/**
 * Intercepts every /api call: there is no backend in these tests. Returns the list of requests that hit
 * /api, so tests can assert what the build asks for (and what it never does).
 */
export async function mockApi(page: Page, { plan = 'free', credentials }: MockOptions = {}): Promise<string[]> {
    const calls: string[] = [];
    let refreshValid = false;
    await page.route('**/api/**', route => {
        const path = new URL(route.request().url()).pathname;
        calls.push(`${route.request().method()} ${path}`);
        const json = (body: unknown, status = 200) =>
            route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });

        if (path === '/api/auth/me') {
            return json({
                id: 'e2e-user', username: 'e2e', email: 'e2e@example.test', isAdmin: false,
                subscription: { id: 's1', plan, features: '{}' },
            });
        }
        if (path === '/api/subscription/me') {
            return json({ currentPlan: plan, hasPendingProcess: false, options: TIERS });
        }
        if (path.startsWith('/api/sites/public/preview/')) {
            return json({ siteName: 'Demo site', generatedAt: '2026-01-01T00:00:00Z', data: [], topPages: [], hourlyActivity: [], devices: [], locations: [], funnels: [] });
        }
        if (path === '/api/features') {
            return json({ plausibleImport: false, ga4Import: false, notifications: false, heatmaps: false });
        }
        if (credentials && path === '/api/auth/login') {
            const body = route.request().postDataJSON() ?? {};
            if (body.username !== credentials.username || body.password !== credentials.password) return json({}, 401);
            refreshValid = true;
            return json({ accessToken: fakeAccessToken(), refreshToken: REFRESH_TOKEN, expiresIn: 1800 });
        }
        if (credentials && path === '/api/auth/logout') {
            refreshValid = false;
            return json({});
        }
        if (credentials && path === '/api/auth/refresh') {
            const body = route.request().postDataJSON() ?? {};
            if (!refreshValid || body.refreshToken !== REFRESH_TOKEN) return json({}, 401);
            return json({ accessToken: fakeAccessToken(), refreshToken: REFRESH_TOKEN, expiresIn: 1800 });
        }
        if (path === '/api/auth/refresh') return json({}, 401);
        // Anything else: an empty answer keeps the pages rendering without a backend.
        return json([]);
    });
    return calls;
}
