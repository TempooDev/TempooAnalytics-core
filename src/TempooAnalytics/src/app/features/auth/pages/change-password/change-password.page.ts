import { Component, signal, inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '@app/shared/services/auth.service';

@Component({
    standalone: true,
    imports: [CommonModule, FormsModule],
    templateUrl: './change-password.page.html',
    styleUrls: ['./change-password.page.css'],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChangePasswordPage {
    private auth = inject(AuthService);
    private router = inject(Router);

    oldPassword = signal('');
    newPassword = signal('');
    loading = signal(false);
    error = signal<string | null>(null);

    async submit() {
        this.error.set(null);
        this.loading.set(true);
        try {
            await this.auth.changePassword({ oldPassword: this.oldPassword(), newPassword: this.newPassword() });
            await this.router.navigateByUrl('/');
        } catch (err) {
            this.error.set('Change password failed');
        } finally {
            this.loading.set(false);
        }
    }
}
