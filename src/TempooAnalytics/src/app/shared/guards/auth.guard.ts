import { Injectable, inject } from '@angular/core';
import { CanActivate, Router, ActivatedRouteSnapshot, RouterStateSnapshot, UrlTree } from '@angular/router';
import { AuthService } from '../services/auth.service';

@Injectable({ providedIn: 'root' })
export class AuthGuard implements CanActivate {
    private auth = inject(AuthService);
    private router = inject(Router);

    async canActivate(route: ActivatedRouteSnapshot, state: RouterStateSnapshot): Promise<boolean | UrlTree> {
        if (this.auth.isAuthenticated()) return true;
        const restored = await this.auth.tryRestoreSession();
        if (restored) return true;
        return this.router.parseUrl('/auth/login');
    }
}
