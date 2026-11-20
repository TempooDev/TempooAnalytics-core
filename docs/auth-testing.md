ç
# Auth Testing

This document lists manual test steps and basic automated tests added for the authentication flows.

## Dev test credentials
- Username: `dev@tempoo.local`
- Password: `dev`

These are created automatically when the API runs in Development (see `Program.cs`). The login page shows these values in development.

## Manual test scenarios

1) Login (happy path)
- Start backend: `dotnet run --project TempooAnalytics.Api`
- Start frontend: `cd src/TempooAnalytics && npm run start`
- Open: `http://localhost:4200/auth/login`
- Use dev credentials (or sign up a new user) and press *Entrar*.
- Expected: redirected to `/` and the header shows the account dropdown.
- Verify cookie: open DevTools → Application → Cookies → see `refresh_token` set for the API origin.

2) Forgot password (placeholder)
- On the login page click *Recordar contraseña*.
- Enter an email and press *Enviar*.
- Expected: simulated success message is shown. (Backend endpoint `/auth/forgot` is not implemented.)

3) User sign-up
- There is no public sign-up: `/auth/signup` redirects to the login. Users are created by an administrator (`/admin/users` or `POST /api/admin/signup`); see docs/selfhost-first-run.md.

4) Change password
- Log in, open header → Account → Change password.
- Enter old and new password and submit.
- Expected: success and can login again with new password.

5) Refresh flow
- After login, inspect outgoing requests to the API: `Authorization: Bearer <token>` header must be present.
- When access token expires, API should return 401 and frontend should call `/auth/refresh` using the httpOnly cookie and retry the request.

## Automated (basic) unit tests added
- `src/app/features/auth/pages/login.page.spec.ts` — renders component and asserts dev credentials appear in dev.
- `src/app/features/auth/pages/forgot-password.page.spec.ts` — renders component and asserts form elements.

These are simple sanity tests (component compiles and shows expected elements). They are not full e2e tests — add e2e tests separately if you want browser-level flows.

## How to run tests
- Angular unit tests:

```powershell
cd src/TempooAnalytics
npm run test
```

- .NET tests (if available):

```powershell
dotnet test TempooAnalytics.Api.Tests/TempooAnalytics.Api.Tests.csproj
```

## Playwright E2E

The old scaffold (`e2e/tests/auth.spec.ts`, signup -> login against a running API) was removed: its routes and selectors were out of date, its own config was never run, and `/api/auth/signup` does not exist. The e2e tests now live in `src/TempooAnalytics/e2e/editions` and cover login, reload with an active session, wrong credentials, logout and `/auth/login` with a session, in both the community and cloud builds. They serve the static bundles and intercept `/api` (no backend):

```bash
cd src/TempooAnalytics
npm run build && npm run build:community
npx playwright install chromium   # once
npm run test:e2e:editions
```

See `docs/selfhost-first-run.md` for how the first user is created on a self-host.

## Next steps / recommendations
- Add end-to-end tests (Cypress/Playwright) to cover signup→login→change-password and refresh flows.
- Implement `/auth/forgot` backend endpoint and email sending.
- Add integration tests for admin endpoints (assign site, set subscription).

If you want, I can now run the Angular unit tests locally (if you allow) or add Cypress e2e tests. Which do you prefer next?
