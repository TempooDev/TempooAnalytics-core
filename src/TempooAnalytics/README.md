# TempooAnalytics

This project was generated using [Angular CLI](https://github.com/angular/angular-cli) version 20.3.5.

## Development server

To start a local development server, run:

```bash
ng serve
```

Once the server is running, open your browser and navigate to `http://localhost:4200/`. The application will automatically reload whenever you modify any of the source files.

## Code scaffolding

Angular CLI includes powerful code scaffolding tools. To generate a new component, run:

```bash
ng generate component component-name
```

For a complete list of available schematics (such as `components`, `directives`, or `pipes`), run:

```bash
ng generate --help
```

## Building

To build the project run:

```bash
ng build
```

This will compile your project and store the build artifacts in the `dist/` directory. By default, the production build optimizes your application for performance and speed.

## Running unit tests

To execute unit tests with the [Karma](https://karma-runner.github.io) test runner, use the following command:

```bash
ng test
```

## Running end-to-end tests

For end-to-end (e2e) testing, run:

```bash
ng e2e
```

Angular CLI does not come with an end-to-end testing framework by default. You can choose one that suits your needs.

## Additional Resources

For more information on using the Angular CLI, including detailed command references, visit the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.

## Tempoo public client script (usage)

This project ships a lightweight public client script used to send events from customer pages to the Tempoo Analytics API. Quick reference for attributes supported on the script tag and how the client sends requests:

- `data-site-id`: the public `site_key` for the site. Example: `<script data-site-id="landing-tempoo" data-api-url="https://analytics.example.com/analytics/events" src="/tempoo-analytics.js"></script>`.
- `data-write-key` (optional): a per-site write key used to authenticate requests coming from the site. When present the client will set `Authorization: Bearer <write_key>` and `X-API-Key: <write_key>` on `fetch` requests. For `navigator.sendBeacon` (which cannot set headers) the client falls back to appending `?key=<write_key>` to the endpoint URL so servers can validate the key.

Notes:
- Prefer using the `write_key` when possible; it is more robust and works even if the Origin header is absent or the page is embedded in an iframe.
- If you don't provide a `write_key`, the server will validate the request by comparing the `Origin` (or `Referer`) header against the `allowed_origins` list configured for the site.
- The public `site_key` (`data-site-id`) is safe to expose in client pages; the `write_key` should be treated as a secret for that site and rotated if leaked.

Server-side: the ingest endpoint accepts:
- `Authorization: Bearer <write_key>` header (preferred), or
- `X-API-Key: <write_key>` header, or
- `?key=<write_key>` query parameter (used by sendBeacon fallback), or
- `Origin`/`Referer` header matching a configured `allowed_origins` entry for the site.

See `tools/tempoo-analytics/tempoo-analytics.js` for implementation details and the `Initial` EF Core migration in `src/TempooAnalytics.Api/Data/Migrations` for the DB schema (`write_key`, `allowed_origins`).
