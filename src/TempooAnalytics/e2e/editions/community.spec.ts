import { test, expect } from '@playwright/test';
import { readdirSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { mockApi, signIn } from '../support/mock-api';
import { defineAuthFlows } from '../support/auth-flows';

// Community build (main.ts, no app/cloud): served statically, /api intercepted.

const BILLING_TEXT = /Current Subscription|Modify Subscription|Upgrade Your Plan|AI Credits|Weekly Digest|API Keys|Payment Successful/i;

test('the app loads and shows the login screen without a session', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', e => errors.push(e.message));
    await mockApi(page);
    await page.goto('/');
    await expect(page).toHaveURL(/\/auth\/login/);
    await expect(page.locator('input[type="password"]')).toBeVisible();
    expect(errors).toEqual([]);
});

test('an authenticated session reaches the app and no subscription endpoint is requested', async ({ page }) => {
    const calls = await mockApi(page);
    await signIn(page);
    await page.goto('/account/profile');
    await expect(page.getByText('e2e@example.test')).toBeVisible();
    expect(calls.filter(c => /subscription|public-api|\/ai\//.test(c))).toEqual([]);
});

test('the profile page has no billing, AI credits, weekly digest or API key blocks', async ({ page }) => {
    await mockApi(page);
    await signIn(page);
    await page.goto('/account/profile');
    await expect(page.getByText('Event Usage (30d)')).toBeVisible();
    await expect(page.locator('body')).not.toContainText(BILLING_TEXT);
    await expect(page.locator('[data-testid^="profile-ext-"]')).toHaveCount(0);
});

for (const route of ['/checkout/success', '/checkout/success?checkout_id=abc', '/account/subscription', '/account/billing']) {
    test(`${route} is not a screen of the community build`, async ({ page }) => {
        await mockApi(page);
        await signIn(page);
        await page.goto(route);
        await page.waitForLoadState('networkidle');
        await expect(page.locator('body')).not.toContainText(BILLING_TEXT);
        await expect(page.getByText('Transaction ID')).toHaveCount(0);
    });
}

defineAuthFlows('community');

test('the login explains that there is no public sign-up and how users are created', async ({ page }) => {
    await mockApi(page);
    await page.goto('/auth/login');
    const hint = page.getByTestId('login-no-signup');
    await expect(hint).toBeVisible();
    await expect(hint).toContainText('ADMIN_PASSWORD');
    await expect(hint).toContainText('/admin/users');
});

test('/auth/signup is not a screen of the community build: it goes back to the login', async ({ page }) => {
    const calls = await mockApi(page);
    await page.goto('/auth/signup');
    await expect(page).toHaveURL(/\/auth\/login/);
    await expect(page.getByRole('button', { name: /sign up|register|create account/i })).toHaveCount(0);
    expect(calls.filter(c => c.includes('/auth/signup'))).toEqual([]);
});

test('the public preview has no trial call to action or lead capture', async ({ page }) => {
    const calls = await mockApi(page);
    await page.goto('/preview/tok123');
    await expect(page.getByText('Demo site')).toBeVisible();
    await expect(page.getByRole('button', { name: /Start 15-day Trial/ })).toHaveCount(0);
    expect(calls.filter(c => c.includes('/api/leads'))).toEqual([]);
});

test('the bundle has nothing from app/cloud', () => {
    const dist = resolve(process.env.E2E_COMMUNITY_DIST ?? 'dist/community/browser');
    const files = readdirSync(dist).filter(f => f.endsWith('.js'));
    expect(files.length).toBeGreaterThan(0);
    const forbidden = /app\/cloud|subscription\/me|subscription\/invoices|public-api\/usage|ai\/credits|checkout\/success|provideCloudFeatures|\/leads|app-lead-modal|Join the Beta/;
    const leaks = files.filter(f => forbidden.test(readFileSync(join(dist, f), 'utf8')));
    expect(leaks, `leaked in ${leaks.join(', ')}`).toEqual([]);
});

test('the bundle carries no hosted-service host, personal address or plan wording', () => {
    const dist = resolve(process.env.E2E_COMMUNITY_DIST ?? 'dist/community/browser');
    const files = readdirSync(dist, { recursive: true, encoding: 'utf8' }).filter(f => /\.(js|html)$/.test(f));
    expect(files.length).toBeGreaterThan(0);
    const forbidden = /tempooanalytics\.(eu|com)|@gmail\.com|@google\.com|Pro feature|Unlock Funnels|Unlock Leads|Your Freelance trial|hasLeads|hasAdvancedAnalytics/;
    const leaks = files.filter(f => forbidden.test(readFileSync(join(dist, f), 'utf8')));
    expect(leaks, `leaked in ${leaks.join(', ')}`).toEqual([]);
});

test('the header brand is neutral and /feedback shows no contact address when none is configured', async ({ page }) => {
    await mockApi(page);
    await signIn(page);
    await page.goto('/feedback');
    await expect(page.locator('header')).toContainText('Tempoo Analytics');
    await expect(page.getByText('Direct Support')).toHaveCount(0);
    await expect(page.locator('a[href^="mailto:"]')).toHaveCount(0);
});
