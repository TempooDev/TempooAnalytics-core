import { HttpInterceptorFn, HttpErrorResponse, HttpContextToken } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ToastService } from '@app/shared/services/toast.service';

/** Background requests (e.g. polling) set this so a failure doesn't show a toast every time. */
export const SILENT_ERRORS = new HttpContextToken<boolean>(() => false);

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const toastSvc = inject(ToastService);

  return next(req).pipe(
    catchError((error: any) => {
      if (req.context.get(SILENT_ERRORS)) return throwError(() => error);

      console.error('[ErrorInterceptor] Request failed:', req.url, error);
      
      let message = 'An unexpected error occurred';
      let type: 'error' | 'warning' = 'error';

      if (error instanceof HttpErrorResponse) {
        // Custom messages based on status code
        switch (error.status) {
          case 0:
            message = 'Unable to connect to the server. Please check your internet connection.';
            type = 'warning';
            break;
          case 400:
            message = error.error?.message || error.error?.error || 'Bad request. Please check your input.';
            break;
          case 401:
            // Handled by authInterceptor (refresh logic)
            return throwError(() => error);
          case 403:
            message = 'You do not have permission to perform this action.';
            break;
          case 404:
            message = 'The requested resource was not found.';
            break;
          case 500:
            message = 'Server error. Our team has been notified.';
            break;
          default:
            message = error.error?.message || error.error?.error || message;
            break;
        }
      } else {
        // Generic error (not HTTP response)
        message = error?.message || 'A network error occurred';
        type = 'warning';
      }

      if (type === 'error') {
          toastSvc.error(message);
      } else {
          toastSvc.warning(message);
      }
      
      return throwError(() => error);
    })
  );
};
