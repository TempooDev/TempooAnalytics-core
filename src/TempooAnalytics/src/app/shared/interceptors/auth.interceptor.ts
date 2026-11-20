import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { catchError, switchMap } from 'rxjs/operators';
import { throwError, from } from 'rxjs';
import { AuthService } from '@app/shared/services/auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
    const authSvc = inject(AuthService);
    const router = inject(Router);
    const token = authSvc.token;

    const url = req.url || '';
    const isPublicEndpoint = url.includes('/api/sites/public/preview/') || 
                           url.includes('/api/public/tiers') ||
                           url.includes('/analytics/events');

    // Only attach token if NOT a public endpoint and token exists
    const authReq = (token && !isPublicEndpoint) ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

    return next(authReq).pipe(
        catchError((err: unknown) => {
            if (err instanceof HttpErrorResponse && err.status === 401) {
                const isAuthEndpoint = url.includes('/auth/refresh') ||
                    url.includes('/auth/token') ||
                    url.includes('/auth/login') ||
                    url.includes('/auth/logout');

                if (isAuthEndpoint || isPublicEndpoint) {
                    return throwError(() => err);
                }

                return from(authSvc.refreshToken()).pipe(
                    switchMap((newToken) => {
                        if (newToken) {
                            const retryReq = req.clone({ setHeaders: { Authorization: `Bearer ${newToken}` } });
                            return next(retryReq);
                        }
                        authSvc.clearLocalSession();
                        router.navigate(['/auth/login']);
                        return throwError(() => err);
                    }),
                    catchError((refreshErr) => {
                        authSvc.clearLocalSession();
                        router.navigate(['/auth/login']);
                        return throwError(() => refreshErr);
                    })
                );
            }
            return throwError(() => err);
        })
    );
};
