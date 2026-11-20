# Running the community edition locally

The AppHost starts the core stack for local development: the core API (`TempooAnalytics.Api`), PostgreSQL, ClickHouse, Redis, the local S3, the ingestion and importer services, the gateway and the community frontend (`web`, http://localhost:4200, which runs `npm run start:community`).

## Starting it

```bash
aspire run --project src/TempooAnalytics.AppHost
```

or, without the Aspire CLI:

```bash
dotnet run --project src/TempooAnalytics.AppHost
```

## Checking the edition

In the Aspire dashboard, the command of the `web` resource must be `start:community`. The community app has no plans, subscriptions, upgrade prompts or public API keys.

## How it is built

`AppHost.cs` holds the topology; what an edition adds is in `AppHostEdition.cs`. This repository carries the core edition on its own, so there is nothing to switch: the `Parameters:WebEdition` setting has no effect here.
