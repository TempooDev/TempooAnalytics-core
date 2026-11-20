import { defineConfig, devices } from '@playwright/test';

// e2e of the two builds, served as static bundles with /api intercepted (no backend).
// Build first: `npm run build` (cloud) and `npm run build:community`. Run: `npm run test:e2e:editions`.
const CLOUD_PORT = 4310;
const COMMUNITY_PORT = 4311;
const serve = (dir: string, port: number) => ({
    command: `node e2e/support/serve-static.mjs ${dir} ${port}`,
    url: `http://127.0.0.1:${port}/`,
    reuseExistingServer: !process.env.CI,
});

export default defineConfig({
    testDir: './e2e/editions',
    timeout: 30_000,
    expect: { timeout: 7_000 },
    fullyParallel: true,
    retries: process.env.CI ? 1 : 0,
    reporter: process.env.CI ? [['list'], ['github']] : [['list']],
    use: { headless: true, viewport: { width: 1280, height: 800 }, trace: 'off' },
    webServer: [
        serve('dist/TempooAnalytics/browser', CLOUD_PORT),
        serve('dist/community/browser', COMMUNITY_PORT),
    ],
    projects: [
        { name: 'community', testMatch: 'community.spec.ts', use: { ...devices['Desktop Chrome'], baseURL: `http://127.0.0.1:${COMMUNITY_PORT}` } },
        { name: 'cloud', testMatch: 'cloud.spec.ts', use: { ...devices['Desktop Chrome'], baseURL: `http://127.0.0.1:${CLOUD_PORT}` } },
    ],
});
