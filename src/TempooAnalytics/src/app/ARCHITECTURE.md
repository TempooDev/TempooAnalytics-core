# Angular Application Architecture

## 📁 Project Structure

```
src/app/
├── features/              # Feature modules
│   ├── dashboard/        # Feature: Analytics dashboard
│   ├── sites-list/       # Feature: Sites list
│   └── custom-events-groups/  # Feature: Event grouping
├── shared/               # Shared code
│   ├── components/      # Reusable components
│   └── services/        # Global services
├── app.config.ts        # Application configuration
├── app.routes.ts        # Route definitions
└── app.ts              # Root component
```

## 🎯 Architecture Principles

### 1. **Feature-Based Organization**
Each feature has its own folder with:
- **pages/**: Page components (routable)
- **components/**: Components specific to the feature
- **services/**: Specific services (optional)
- **index.ts**: Barrel exports for clean imports

### 2. **Shared Module**
Contains code that can be reused across features:
- Generic UI components
- Global services
- Shared pipes and directives
- Common models and interfaces

### 3. **Separation of Concerns**
- ✅ Features are independent of each other
- ✅ Features can import from `shared/`
- ❌ `shared/` MUST NOT import from `features/`
- ❌ Features MUST NOT import from other features

## 📦 Features

### Dashboard
**Route**: `/analytics/site/:siteKey`  
**Description**: Visualization of the metrics and charts of a specific site

**Components:**
- `site-dashboard.page.ts` - Main dashboard page
- `custom-events-chart.component.ts` - Custom events chart

**Dependencies:**
- `AnalyticsService` (shared)
- `MetricCardComponent` (shared)
- `SitesListComponent` (sites-list feature)

### Sites List
**Route**: Embedded in the dashboard  
**Description**: List of all sites with their basic metrics

**Components:**
- `sites-list.page.ts` - Sites table

**Dependencies:**
- `AnalyticsService` (shared)

### Custom Events Groups
**Route**: `/analytics/custom-events/groups`  
**Description**: Explorer and grouper of custom events

**Components:**
- `custom-events-groups.page.ts` - Exploration page

**Dependencies:**
- `AnalyticsService` (shared)
- PrimeNG components

## 🔧 Best Practices Applied

### Angular 20+ Features
- ✅ **Signals** for reactive state
- ✅ **Computed signals** for derived state
- ✅ **Effects** for side effects
- ✅ **input()** and **output()** instead of decorators
- ✅ **inject()** instead of constructor injection
- ✅ **OnPush** change detection in all components
- ✅ Modern control flow (`@if`, `@for`, `@switch`)
- ✅ No `standalone: true` (the default in Angular 20+)

### Organization
- ✅ Barrel exports (`index.ts`) for clean imports
- ✅ Clear relative paths
- ✅ Separation by responsibility
- ✅ Small, focused components

## 📝 Naming Conventions

### Files
- **Pages**: `*.page.ts` / `*.page.html`
- **Components**: `*.component.ts` / `*.component.html`
- **Services**: `*.service.ts`
- **Models**: `*.model.ts` / `*.interface.ts`

### Classes
- **Page components**: `*Page` or `*PageComponent`
- **Regular components**: `*Component`
- **Services**: `*Service`

## 🚀 How to Add a New Feature

1. Create a folder in `features/`:
   ```bash
   mkdir features/nueva-feature
   mkdir features/nueva-feature/pages
   mkdir features/nueva-feature/components  # if needed
   ```

2. Create the necessary files:
   ```
   nueva-feature/
   ├── index.ts
   ├── pages/
   │   ├── nueva-feature.page.ts
   │   └── nueva-feature.page.html
   └── components/  # optional
   ```

3. Export it in `index.ts`:
   ```typescript
   export * from './pages/nueva-feature.page';
   ```

4. Add a route in `app.routes.ts`:
   ```typescript
   import { NuevaFeaturePage } from './features/nueva-feature';
   
   { path: 'ruta', component: NuevaFeaturePage }
   ```

## 🔍 Imports

### ✅ Correct
```typescript
// Use barrel exports
import { AnalyticsService } from '../shared/services';
import { SiteDashboardComponent } from './features/dashboard';
```

### ❌ Avoid
```typescript
// Do not use direct paths when a barrel export exists
import { AnalyticsService } from '../shared/services/analytics.service';
import { SiteDashboardComponent } from './features/dashboard/pages/site-dashboard.page';
```

## 📚 Resources

- [Angular Style Guide](https://angular.dev/style-guide)
- [Angular Signals](https://angular.dev/guide/signals)
- [Feature Modules Best Practices](https://angular.dev/guide/modules)
