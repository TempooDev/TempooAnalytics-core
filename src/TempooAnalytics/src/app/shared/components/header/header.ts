import { Component, computed, inject, signal, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { APP_SETTINGS } from '@app/core/app-settings';
import { ENTITLEMENTS } from '@app/core/entitlements';
import { UPSELL_COMPONENT } from '@app/core/upsell';
import { AuthService } from '@app/shared/services/auth.service';
import { ThemeService } from '@app/shared/services/theme.service';
import { FeatureFlagsService } from '@app/shared/services/feature-flags.service';
import { NotificationBellComponent } from './notification-bell/notification-bell';

@Component({
  selector: 'app-header',
  standalone: true,
  imports: [CommonModule, NotificationBellComponent],
  templateUrl: './header.html',
})
export class HeaderComponent {
  router = inject(Router);
  private auth = inject(AuthService);
  themeService = inject(ThemeService);
  notificationsEnabled = inject(FeatureFlagsService).notifications;

  // expose token signal so template can reactively show auth UI
  token = this.auth.tokenSignal();
  private entitlements = inject(ENTITLEMENTS);
  planFeatures = this.entitlements.features;
  currentPlan = computed(() => this.entitlements.plan() ?? '');
  hasPlan = computed(() => this.entitlements.plan() !== null);
  settings = inject(APP_SETTINGS);
  upsell = inject(UPSELL_COMPONENT);
  // dropdown open state
  menuOpen = signal(false);

  goHome() {
    this.router.navigate(['/']);
  }

  addSite() {
    this.router.navigate(['/account/sites'], { queryParams: { action: 'add' } });
  }

  async logout() {
    await this.auth.logout();
    this.router.navigate(['/auth/login']);
  }

  toggleMenu() {
    this.menuOpen.update(v => !v);
  }

  closeMenu() {
    this.menuOpen.set(false);
  }

  @HostListener('document:click')
  onDocumentClick() {
    this.closeMenu();
  }
}
