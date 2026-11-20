# Self-host: first run

What happens the first time a community instance starts (without the billing module) and how users are created. None of this depends on a hosted service: no payments provider, no Google, no email.

The edition is inferred from the loaded modules (`Infrastructure/Modules/Edition.cs`): cloud means "a billing endpoints module (`IBillingEndpointsModule`) is loaded"; community means none is (it is not distributed, or `Modules:Enabled=false`).

## First administrator

On startup, **only if** the edition is community, the environment is not Development and the users table is empty, the API creates an administrator through the normal sign-up flow (`IUserManagementService.SignupAsync`), with no welcome email (there is no Resend dependency).

| Variable | Purpose |
|---|---|
| `ADMIN_USERNAME` (or `Bootstrap:AdminUsername`) | Administrator name. Defaults to `admin`. Stored in lowercase. |
| `ADMIN_PASSWORD` (or `Bootstrap:AdminPassword`) | Password (8 characters minimum). If missing, a 24-character one is generated with a cryptographic generator. |

- **With `ADMIN_PASSWORD`:** the password is not written to any log; only the fact that the user was created is recorded.
- **Without `ADMIN_PASSWORD`:** the generated password is written **only once** to the API log (line `FIRST RUN ... Password: ...`, Warning level). Sign in and change it (Account menu, Change Password). If you do not copy it, it is lost: delete the user, or set `ADMIN_PASSWORD` with an empty database and restart.
- **Idempotent:** if any user already exists it does nothing (it neither recreates nor resets; changing `ADMIN_PASSWORD` afterwards does not change the password of an existing account). With a password shorter than 8 characters it creates nothing and says so in the log.
- **Never in cloud:** with the billing module loaded nothing is created, even if the variables are set.
- **Development:** the existing seed of the `dev@tempoo.local` user is kept.

## JWT signing key (`Auth:JwtKey`)

The key that signs the tokens no longer has a public default value outside Development.

| Situation | Behaviour |
|---|---|
| `Auth:JwtKey` / `AUTH_JWTKEY` defined | It is used, in any edition. |
| Development, `Testing` or `TEST_EXECUTION=true` | Fixed development key (only so a checkout and the tests can start). |
| Cloud without a key | **The API does not start**, with a clear message. |
| Community without a key | Generates 64 random bytes once and stores them in `Auth:JwtKeyPath` (default `Data/jwt.key`, the same folder as the GeoLite2 files), with 0600 permissions. Restarts reuse the file, so sessions survive. |

Why a file rather than "always fail": a self-host must start without a prior manual step, and regenerating the key on every start would sign everyone out on every restart. If the file cannot be created (read-only folder) the API stops and says how to fix it: define `AUTH_JWTKEY`, or point `Auth:JwtKeyPath` at a persistent volume. In Docker, mount a volume on `/app/Data` (GeoLite2 needs it too) or define `AUTH_JWTKEY`. Anyone who has the file can issue tokens for any user: do not include it in backups that you share.

## User sign-up (community and cloud)

There is no public sign-up in either edition. The old `/auth/signup` page called `POST /api/auth/signup`, which never existed (there is only `POST /api/admin/signup`, which requires an administrator), and nothing linked to it. It was removed: `/auth/signup` redirects to the login in both editions. Users are created as follows:

1. The first administrator of a self-host is created on startup (above).
2. An administrator creates the other users from the admin console, `/admin/users` (unlocked with `ADMIN_MASTER_PASSWORD`), or through the API with `POST /api/admin/signup` and their token. In cloud, sign-up is by administrator invitation only and sends the welcome email with the credentials.

The community login explains how users are created.

## No plan limits in community

Community has no plans: every user gets everything, with no quotas. Unlimited sites, import (Plausible, GA4), export, advanced filters, funnels and heatmaps are all available. Data retention is unlimited unless `Retention:Months` is set. The one thing outside the API is the ingestion worker (Go), which reads the plan from the database: run it with `DISABLE_PLAN_LIMITS=true` in a self-host, otherwise sites without a subscription get the cloud's 15-day trial and a 10,000 events/month quota. Do not set it in cloud.

## Tests

- API: `JwtKeyProviderTests` (unit) and `FirstAdminBootstrapIntegrationTests` (Testcontainers: community with and without variables, existing users, restart, cloud, short password).
- Frontend: vitest (`guest.guard.spec.ts`, `login.page.spec.ts`) and e2e for both editions in `e2e/editions` (`npm run test:e2e:editions`): login, reload with a session (with refresh of the expired token), wrong credentials, logout and `/auth/login` with an active session.

## For the self-host README (task 5.4)

Text to include in the installation section:

> **First run.** Set `ADMIN_USERNAME` and `ADMIN_PASSWORD` before the first start and that account is created as administrator. If you leave `ADMIN_PASSWORD` empty, a random password is generated and printed once in the API log (`FIRST RUN`): sign in and change it. There is no public sign-up; administrators add users from `/admin/users`. Mount a persistent volume on `/app/Data` (or set `AUTH_JWTKEY`) so the signing key survives restarts. The `.env.example` of the self-host compose must list `ADMIN_USERNAME`, `ADMIN_PASSWORD`, `AUTH_JWTKEY` (optional) and `ADMIN_MASTER_PASSWORD`.
