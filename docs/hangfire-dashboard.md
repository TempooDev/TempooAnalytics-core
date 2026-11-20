# Hangfire Dashboard (Developer + Production access)

This project exposes a lightweight Hangfire Dashboard host used for debugging and management of background jobs.

How it runs

Security
  - Environment variable `HANGFIRE_DASHBOARD_KEY`, or
  - Configuration `Hangfire:DashboardKey` in appsettings.

Aspire / AppHost notes
- The AppHost builder used in this repository currently exposes the worker's HTTP endpoint via `.WithHttpEndpoint()` so Aspire can route to the worker's dashboard.
- To inject the dashboard secret when Aspire builds/publishes the worker image, you can provide the build argument or env var at image build/run time.
- This repository includes a Dockerfile for the worker (`src/TempooAnalytics.EventWorker/Dockerfile`) that supports a build argument `HANGFIRE_DASHBOARD_KEY` and sets it as an image environment variable.

Examples
- Build the worker image with a secret (CI/aspire build step can pass the ARG):
```powershell
docker build -t tempooanalytics-eventworker:dev --build-arg HANGFIRE_DASHBOARD_KEY="my-secret-key" -f src/TempooAnalytics.EventWorker/Dockerfile .
```

- Run with docker-compose override (or platform secret injection):
```yaml
services:
  tempooanalytics-eventworker:
    image: tempooanalytics-eventworker:dev
    environment:
      - HANGFIRE_DASHBOARD_KEY=${HANGFIRE_DASHBOARD_KEY}
```

If Aspire provides a mechanism to inject environment variables or secrets into published containers (check your AppHost/aspire configuration or platform UI), set `HANGFIRE_DASHBOARD_KEY` there for the worker service. If you want, I can add an example AppHost integration if you tell me which Aspire version/extension you are using to manage secrets.

Access methods (pick one):

Example (PowerShell):

```powershell
$env:HANGFIRE_DASHBOARD_KEY = "<your-dashboard-key>"
# Start worker (example)
dotnet run --project src/TempooAnalytics.EventWorker/TempooAnalytics.EventWorker.csproj

# Access with curl (Authorization header)
curl -H "Authorization: Bearer <your-dashboard-key>" http://localhost:5800/hangfire

# Or with header
curl -H "X-Dashboard-Key: <your-dashboard-key>" http://localhost:5800/hangfire
```

Notes
