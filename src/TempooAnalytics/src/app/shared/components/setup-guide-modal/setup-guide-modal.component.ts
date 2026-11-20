import { Component, Input, Output, EventEmitter, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { APP_SETTINGS } from '@app/core/app-settings';
import { ToastService } from '@app/shared/services/toast.service';
import { ENTITLEMENTS } from '@app/core/entitlements';
import { FeatureFlagsService } from '@app/shared/services/feature-flags.service';

@Component({
  selector: 'app-setup-guide-modal',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './setup-guide-modal.component.html',
})
export class SetupGuideModalComponent {
  @Input() siteId: string = '';
  @Input() writeKey: string = '';
  @Output() close = new EventEmitter<void>();

  private router = inject(Router);
  private toast = inject(ToastService);
  private settings = inject(APP_SETTINGS);

  hasPlans = inject(ENTITLEMENTS).hasPlans;
  heatmapsEnabled = inject(FeatureFlagsService).heatmaps;

  copied = signal(false);

  goToDocs() {
    this.router.navigate(['/docs']);
    this.close.emit();
  }

  onClose() {
    this.close.emit();
  }

  copyToClipboard(text: string) {
    navigator.clipboard.writeText(text).then(() => {
      this.copied.set(true);
      this.toast.show('Script copied to clipboard!');
      setTimeout(() => this.copied.set(false), 3000);
    });
  }

  get scriptTag(): string {
    const apiUrl = this.settings.trackingApiUrl ? `\n  data-api-url="${this.settings.trackingApiUrl}"` : '';
    return `<script 
  src="${this.settings.trackingScriptUrl}" 
  data-site="${this.siteId || 'YOUR_SITE_ID'}" 
  data-key="${this.writeKey || 'YOUR_WRITE_KEY'}"${apiUrl}>
</script>`;
  }
}
