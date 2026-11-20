import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

/**
 * For pages that only make sense without a session (login): with a valid session the user goes to the dashboard.
 * The session was already restored at startup (APP_INITIALIZER), so the stored token is enough to decide.
 */
export const guestGuard: CanActivateFn = () => {
    const auth = inject(AuthService);
    const router = inject(Router);
    const token = auth.token;
    return !(token && auth.isTokenValid(token)) || router.parseUrl('/');
};
