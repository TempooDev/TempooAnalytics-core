# Shared - Shared Components and Services

This folder contains all the components, services, pipes, directives and utilities that are **shared across multiple features**.

## Structure

```
shared/
├── components/          # Reusable components
│   ├── metric-card/
│   └── ...
├── services/           # Global services
│   ├── analytics.service.ts
│   └── index.ts
├── pipes/             # Custom pipes (future)
├── directives/        # Custom directives (future)
└── models/           # Shared interfaces and types (future)
```

## Current Components

### metric-card
Reusable metric card that shows a title and a value.

**Usage:**
```typescript
<app-metric-card title="Pageviews" [value]="pageviewsTotal()" />
```

## Current Services

### AnalyticsService
Main service for interacting with the analytics API.

**Methods:**
- `getSitesSummary()` - Gets a summary of all sites
- `getDailyMetrics(siteKey, days)` - Gets daily metrics
- `getCustomEvents(siteKey, days)` - Gets custom events
- `getGroupedCustomEvents(...)` - Gets event groupings

**Usage:**
```typescript
import { AnalyticsService } from '@shared/services';

private readonly analytics = inject(AnalyticsService);
```

## TypeScript Aliases

The project uses aliases to simplify imports:

```typescript
// Shared services
import { AnalyticsService } from '@shared/services';

// Shared components
import { MetricCardComponent } from '@shared/components/metric-card/metric-card';

// Features
import { SiteDashboardComponent } from '@features/dashboard';
```

## Principles

1. **Reusability**: Only include code used in 2+ features
2. **No feature dependencies**: Do not import anything from `features/`
3. **Generic**: Components must be as generic as possible
4. **Well documented**: Include JSDoc on shared components and services
