# Self-hosting Tempoo Analytics Community

The public repository ships a Docker Compose stack with the core only: gateway, API (community edition, `Modules__Enabled=false`),
event ingestion (Go), the import/export worker (Go `importer`), web app, PostgreSQL, ClickHouse and Redis. No payments, public API or cloud services. Only the gateway publishes a port.

## Quick start

```bash
cp .env.example .env
# fill the four required secrets (letters and digits only):  openssl rand -hex 24
$EDITOR .env
docker compose up -d --build        # podman compose works too
docker compose ps
```

Open `http://localhost:8000` (`GATEWAY_PORT`) and sign in with `ADMIN_USERNAME` / `ADMIN_PASSWORD`. Put a TLS reverse proxy in front of the gateway for real use.

## Variables (`.env`)

| Variable | Required | Meaning |
|---|---|---|
| `POSTGRES_PASSWORD`, `CLICKHOUSE_PASSWORD`, `REDIS_PASSWORD` | yes | Passwords of the three data stores. Compose refuses to start while empty. |
| `ADMIN_MASTER_PASSWORD` | yes | Unlocks the `/admin/users` console. |
| `ADMIN_USERNAME` (default `admin`), `ADMIN_PASSWORD` | no | First administrator, created only when there are no users. |
| `AUTH_JWTKEY` | no | Session signing key. Empty: generated once and kept in the `api-data` volume (`/app/Data`). |
| `MANAGEMENT_APIKEY` | no | Key of the management endpoints. |
| `GATEWAY_PORT` (default `8000`) | no | Host port of the gateway (app, API and `/analytics/events`). |
| `PUBLIC_URL` (default `http://localhost:8000`) | no | Address your users open (your TLS proxy in front of the gateway). The links and the logo of emails point here. |
| `STORAGE_R2_ENDPOINT`, `STORAGE_R2_ACCESS_KEY`, `STORAGE_R2_SECRET_KEY`, `STORAGE_R2_BUCKET` | no | S3-compatible bucket for Plausible/GA4 import and CSV export (`docs/selfhost-s3-storage.md`). Without it those features are hidden. |
| `INGEST_DEPENDENCY_TIMEOUT` (default `500ms`) | no | Budget of each ingestion dependency call (site config, quota, dedup) before a 503 or skipping the check. See `docs/ingestion-redis-queue.md`. |
| `MAXMIND_ACCOUNT_ID`, `MAXMIND_LICENSE_KEY` | no | Country and ASN by IP (`docs/geoip.md`). Without them everything works, with no country. |
| `POSTGRES_USER`, `POSTGRES_DB` | no | Defaults `tempoo` / `tempoo`. |

**CORS.** The gateway answers cross-origin requests with credentials only for an allow-list: the instance's own origin (derived from the request's scheme and host), local development hosts and whatever you list in `Cors__AllowedOrigins__0`, `Cors__AllowedOrigins__1`... (full origins such as `https://dashboard.example.com`; set them on the `gateway` and `api` services). `/analytics/events` (the tracker) and `/api/public/heatmaps/overlay` accept any origin but never credentials.

The compose file sets `DISABLE_PLAN_LIMITS=true` for ingestion: community has no plans, so no 15-day trial and no monthly event quota.

## First run

Set `ADMIN_USERNAME` and `ADMIN_PASSWORD` before the first start and that account is created as administrator. If you leave `ADMIN_PASSWORD` empty, a random password is generated and printed once in the API log (`docker compose logs api`, line `FIRST RUN`): sign in and change it. There is no public sign-up; administrators add users from `/admin/users` (asks for `ADMIN_MASTER_PASSWORD`). The volume on `/app/Data` (or `AUTH_JWTKEY`) keeps the signing key across restarts. Details: `docs/selfhost-first-run.md`.

## Send a first event

Create a site in the app (Account, Sites), then:

```bash
curl -X POST http://localhost:8000/analytics/events \
  -H 'Content-Type: application/json' -H 'Origin: https://your-site.example' \
  -d '{"siteId":"<siteKey>","type":"pageview","url":"https://your-site.example/","title":"Home"}'
```

`202` means accepted; the event shows in the dashboard within a few seconds. The `Origin` must be one of the site's allowed origins.

## Good to know

Email (password reset, notifications, welcome emails) is optional and goes through Resend: set `RESEND_APIKEY` and `RESEND_FROMEMAIL` (a sender on a domain you verified in Resend) to enable it. Without them nothing is sent, `api.resend.com` is never called and the API only logs one informational line; the admin console tells you when it could not email a new user's password. `SUPPORT_EMAIL` and `BRAND_NAME` (web app) set the contact address shown on /feedback and /docs and the header text; both are empty by default. To read the numbers through the API (after sending an event): `GET /api/analytics/sites/<siteKey>/top-pages` with the access token from `POST /api/auth/login`.

## Imports, exports and backups

Plausible/GA4 import and CSV export run in the `importer` service and need an S3-compatible bucket (`STORAGE_R2_*`, `docs/selfhost-s3-storage.md`). Without a bucket the app hides them and the API answers "not available on this server"; the worker stays idle. Backups: the `postgres-data` and `clickhouse-data` volumes.
