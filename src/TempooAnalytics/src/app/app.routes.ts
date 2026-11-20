import { Routes } from '@angular/router';
import { featureRoutesFallback, publicFeatureRoutesFallback } from './core/feature-routes';
import { DashboardPage } from '@features/dashboard';
import { LayoutComponent } from './shared/layout/layout';
import { LoginPage } from './features/auth/pages/login/login.page';
import { AuthGuard } from './shared/guards/auth.guard';
import { guestGuard } from './shared/guards/guest.guard';
import { ga4ImportGuard, plausibleImportGuard } from './shared/guards/feature-flag.guard';
// change-password page is lazy-loaded below

export const routes: Routes = [
    {
        path: '',
        component: LayoutComponent,
        canActivate: [AuthGuard],
        children: [
            { path: '', component: DashboardPage },
            {
                path: 'account/change-password',
                loadComponent: () => import('./features/auth/pages/change-password/change-password.page').then(m => m.ChangePasswordPage)
            },
            {
                path: 'account/sites',
                loadComponent: () => import('./features/account/pages/account-sites.page').then(m => m.AccountSitesPage)
            },
            {
                path: 'account/sites/:siteId/imports',
                canActivate: [plausibleImportGuard],
                loadComponent: () => import('./features/imports/pages/import-management.page').then(m => m.ImportManagementPage)
            },
            {
                // Google's OAuth redirect URI (Google:RedirectUri in the API configuration).
                path: 'account/imports/google/callback',
                canActivate: [ga4ImportGuard],
                loadComponent: () => import('./features/imports/pages/ga4-callback.page').then(m => m.Ga4CallbackPage)
            },
            {
                path: 'account/profile',
                loadComponent: () => import('./features/account/pages/profile/profile.page').then(m => m.ProfilePage)
            },
            {
                path: 'docs',
                loadComponent: () => import('./features/docs/docs.page').then(m => m.DocsPage)
            },
            {
                path: 'feedback',
                loadComponent: () => import('./features/feedback/feedback.page').then(m => m.FeedbackPage)
            },
            // Routes contributed through FEATURE_ROUTES (none unless a feature package registers some).
            featureRoutesFallback
        ]
    },
    {
        path: 'auth',
        children: [
            { path: 'login', component: LoginPage, canActivate: [guestGuard] },
            // No public sign-up in either edition (cloud users are invited by an administrator): old links go to the login.
            { path: 'signup', redirectTo: 'login', pathMatch: 'full' },
            { path: 'forgot', loadComponent: () => import('./features/auth/pages/forgot-password/forgot-password.page').then(m => m.ForgotPasswordPage) },
            { path: '', redirectTo: 'login', pathMatch: 'full' }
        ]
    },
    {
        path: 'preview/:token',
        loadComponent: () => import('./public/preview/preview.page').then(m => m.PreviewPage)
    },
    // Root-level routes contributed through PUBLIC_FEATURE_ROUTES (none unless a feature package registers some).
    publicFeatureRoutesFallback
];
