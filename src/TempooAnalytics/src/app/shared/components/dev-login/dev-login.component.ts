import { Component, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AuthService } from '@app/shared/services/auth.service';
import { ToastService } from '@app/shared/services/toast.service';

@Component({
  selector: 'app-dev-login',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div style="padding:12px;background:#021024;color:#e6eef8;border-radius:8px;margin-bottom:12px;">
      <div style="font-weight:600;margin-bottom:6px;">Dev Login</div>
      <div style="font-size:13px;color:#cbd5e1;margin-bottom:8px;">Use a dev token to act as a site owner during local development.</div>
      <div style="display:flex;gap:8px;align-items:center">
        <input [value]="userId()" (input)="userId.set($any($event.target).value)" style="flex:1;padding:8px;border-radius:6px;background:#041229;border:1px solid #0b2340;color:#e6eef8"/>
        <button (click)="login()" style="padding:8px 12px;border-radius:6px;background:#06b6d4;color:#022023;border:none">Sign In</button>
      </div>
      <div style="margin-top:8px;font-size:12px;color:#94a3b8">Default seeded user id used for convenience in local dev.</div>
    </div>
  `
})
export class DevLoginComponent {
  private auth = inject(AuthService);
  private toast = inject(ToastService);

  // default dev user id - adjust if you backfill DB with a different owner
  userId = signal('00000000-0000-0000-0000-000000000001');

  async login() {
    try {
      const res = await this.auth.requestDevToken(this.userId());
      this.toast.show('Dev token acquired');
    } catch (err) {
      this.toast.show('Token request failed');
    }
  }
}
