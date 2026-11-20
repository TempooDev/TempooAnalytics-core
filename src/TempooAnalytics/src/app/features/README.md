# Features - Module Architecture

This folder contains all the features of the application, organised by functionality following the **Feature Modules** pattern.

## Structure

Each feature is organised in its own folder with the following structure:

```
feature-name/
├── index.ts                  # Barrel export to make imports easier
├── pages/                    # Main pages/components of the feature
│   ├── feature.page.ts
│   └── feature.page.html
├── components/              # Components specific to this feature
│   ├── component.ts
│   └── component.html
└── services/               # Services specific to this feature (optional)
    └── feature.service.ts
```

## Current Features

### 1. **sites-list**
List of sites with their basic metrics.
- **Pages**: `sites-list.page.ts`
- **Components**: None specific

### 2. **dashboard**
Main dashboard with metrics and charts.
- **Pages**: `site-dashboard.page.ts`
- **Components**: 
  - `custom-events-chart.component.ts` - Custom events chart

### 3. **custom-events-groups**
Explorer of custom event groupings.
- **Pages**: `custom-events-groups.page.ts`
- **Components**: None specific

## Principles

1. **Cohesion**: Each feature contains everything it needs to work
2. **Encapsulation**: Components specific to a feature are not used outside it
3. **Reuse**: Shared components go in `shared/`
4. **Independence**: Each feature can be developed and tested independently

## Imports

Use the TypeScript aliases to import feature components:

```typescript
// ✅ Correct - Using the @ alias
import { SiteDashboardComponent } from '@features/dashboard';
import { AnalyticsService } from '@shared/services';
import { MetricCardComponent } from '@shared/components/metric-card/metric-card';

// ❌ Avoid - Relative paths
import { SiteDashboardComponent } from '../../../features/dashboard';
import { AnalyticsService } from '../../../shared/services';
```

## Available Aliases

- `@app/*` - Application root
- `@features/*` - Features folder
- `@shared/*` - Shared components and services
- `@environments/*` - Environment configuration
