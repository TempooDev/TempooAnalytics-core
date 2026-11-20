import { Component, signal, inject, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';

@Component({
    standalone: true,
    imports: [CommonModule],
    templateUrl: './forgot-password.page.html',
    styleUrls: ['./forgot-password.page.css'],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ForgotPasswordPage {
    private router = inject(Router);

    email = signal('');
    sending = signal(false);
    message = signal<string | null>(null);

    // placeholder: show message but no backend implementation
    async submit() {
        this.sending.set(true);
        this.message.set(null);
        try {
            // In a full implementation we'd call POST /auth/forgot here
            await new Promise(r => setTimeout(r, 700));
            this.message.set('Se ha enviado un correo de recuperación (simulado). Si no implementado, contacte al administrador.');
        } finally {
            this.sending.set(false);
        }
    }

    back() {
        this.router.navigate(['/auth/login']);
    }
}
