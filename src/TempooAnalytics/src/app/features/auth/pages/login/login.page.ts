import { Component, signal, inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { AuthService } from '@app/shared/services/auth.service';
import { environment } from '@environments/environment';
import { ENTITLEMENTS } from '@app/core/entitlements';

@Component({
    standalone: true,
    imports: [CommonModule],
    templateUrl: './login.page.html',
    styleUrls: ['./login.page.css'],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPage {
    private auth = inject(AuthService);
    private router = inject(Router);

    /** Without plans (self-host) there is no public sign-up: the page says how users are created. */
    readonly selfHost = !inject(ENTITLEMENTS).hasPlans;

    username = signal('');
    password = signal('');
    rememberMe = signal(false);
    loading = signal(false);
    error = signal<string | null>(null);

    // Forgot password state
    showForgotModal = signal(false);
    forgotEmail = signal('');
    forgotLoading = signal(false);
    forgotSuccess = signal(false);
    forgotError = signal<string | null>(null);

    // expose dev credentials in development mode to help testing
    devInfo = signal<{ username: string; password: string } | null>(environment.production ? null : { username: 'dev@tempoo.local', password: 'dev' });

    async submit() {
        this.error.set(null);
        this.loading.set(true);
        try {
            await this.auth.login({ username: this.username(), password: this.password(), rememberMe: this.rememberMe() });
            await this.router.navigateByUrl('/');
        } catch (err: unknown) {
            this.error.set('Login failed');
        } finally {
            this.loading.set(false);
        }
    }

    async requestReset() {
        if (!this.forgotEmail()) return;
        this.forgotError.set(null);
        this.forgotLoading.set(true);
        try {
            await this.auth.forgotPassword(this.forgotEmail());
            this.forgotSuccess.set(true);
        } catch (err: unknown) {
            this.forgotError.set('Failed to request password reset');
        } finally {
            this.forgotLoading.set(false);
        }
    }

    closeForgotModal() {
        this.showForgotModal.set(false);
        this.forgotEmail.set('');
        this.forgotSuccess.set(false);
        this.forgotError.set(null);
    }
}
