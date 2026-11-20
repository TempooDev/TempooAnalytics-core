import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection, APP_INITIALIZER } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { AuthService } from '@app/shared/services/auth.service';
import { providePrimeNG } from 'primeng/config';
import Aura from '@primeuix/themes/aura';

import { routes } from './app.routes';
import { provideAnimations } from '@angular/platform-browser/animations';
import { authInterceptor } from '@app/shared/interceptors/auth.interceptor';
import { errorInterceptor } from '@app/shared/interceptors/error.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideRouter(routes),
    provideAnimations(),
    provideHttpClient(withInterceptors([authInterceptor, errorInterceptor])),
    providePrimeNG({
        theme: {
            preset: Aura,
            options: {
                darkModeSelector: '.my-app-dark'
            }
        },
        ripple: true,
        zIndex: {
            modal: 1100,
            overlay: 11000,
            menu: 1100,
            tooltip: 1100
        }
    }),
    {
      provide: APP_INITIALIZER,
      useFactory: (auth: AuthService) => () => auth.tryRestoreSession(),
      deps: [AuthService],
      multi: true
    }
  ]
};
