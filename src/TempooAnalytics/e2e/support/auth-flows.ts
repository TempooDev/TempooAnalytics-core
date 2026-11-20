import { Page, test, expect } from '@playwright/test';
import { E2E_CREDENTIALS, expiredAccessToken, mockApi, signIn } from './mock-api';

/**
 * Login, reload, logout and "login with a session" against a static bundle with /api intercepted.
 * Registered by both edition specs: the flow is the same in community and cloud.
 */
export function defineAuthFlows(edition: 'community' | 'cloud'): void {
    const { username, password } = E2E_CREDENTIALS;

    async function login(page: Page, user = username, pass = password) {
        await page.goto('/auth/login');
        await page.locator('#username').fill(user);
        await page.locator('#password').fill(pass);
        await page.getByRole('button', { name: 'Sign In' }).click();
    }

    test.describe(`${edition}: login and session`, () => {
        test('a correct login reaches the dashboard and the session survives a reload', async ({ page }) => {
            const calls = await mockApi(page, { credentials: E2E_CREDENTIALS });
            await login(page);

            await expect(page).toHaveURL(/\/$/);
            await expect(page.locator('#account-menu-button')).toBeVisible();
            expect(calls).toContain('POST /api/auth/login');

            await page.reload();
            await expect(page).toHaveURL(/\/$/);
            await expect(page.locator('#account-menu-button')).toBeVisible();

            await page.goto('/account/profile');
            await expect(page.getByText('e2e@example.test')).toBeVisible();
        });

        test('reloading with an expired access token refreshes it and keeps the session', async ({ page }) => {
            const calls = await mockApi(page, { credentials: E2E_CREDENTIALS });
            await login(page);
            await expect(page.locator('#account-menu-button')).toBeVisible();

            await page.evaluate(t => sessionStorage.setItem('access_token', t), expiredAccessToken());
            calls.length = 0;
            await page.reload();

            await expect(page).toHaveURL(/\/$/);
            await expect(page.locator('#account-menu-button')).toBeVisible();
            expect(calls).toContain('POST /api/auth/refresh');
        });

        test('wrong credentials show an error and stay on the login', async ({ page }) => {
            await mockApi(page, { credentials: E2E_CREDENTIALS });
            await login(page, username, 'not-the-password');

            await expect(page.getByRole('alert')).toContainText('Login failed');
            await expect(page).toHaveURL(/\/auth\/login/);
            expect(await page.evaluate(() => sessionStorage.getItem('access_token'))).toBeNull();
        });

        test('logout ends the session: the app goes back to the login and a reload does not bring it back', async ({ page }) => {
            await mockApi(page, { credentials: E2E_CREDENTIALS });
            await login(page);
            await page.locator('#account-menu-button').click();
            await page.getByRole('menuitem', { name: 'Logout' }).click();

            await expect(page).toHaveURL(/\/auth\/login/);
            await page.reload();
            await expect(page).toHaveURL(/\/auth\/login/);
            await page.goto('/account/profile');
            await expect(page).toHaveURL(/\/auth\/login/);
        });

        test('with an active session /auth/login redirects to the dashboard', async ({ page }) => {
            await mockApi(page, { credentials: E2E_CREDENTIALS });
            await signIn(page);
            await page.goto('/auth/login');

            await expect(page).toHaveURL(/\/$/);
            await expect(page.locator('#account-menu-button')).toBeVisible();
        });

        test('without a session /auth/login stays on the login', async ({ page }) => {
            await mockApi(page, { credentials: E2E_CREDENTIALS });
            await page.goto('/auth/login');

            await expect(page).toHaveURL(/\/auth\/login/);
            await expect(page.locator('#password')).toBeVisible();
        });
    });
}
