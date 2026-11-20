import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { GA4_SITE_STORAGE_KEY, Ga4Property, ImportService } from '../../../shared/services/import.service';
import { ToastService } from '../../../shared/services/toast.service';
import { ENTITLEMENTS } from '@app/core/entitlements';
import { ga4AuthorizeErrorMessage } from './import-management.page';

export type Ga4CallbackError = 'cancelled' | 'expired' | 'noProperties' | 'conflict' | 'other';

/**
 * Google sends the user back here after the consent screen. The page hands the code to the API, lets the
 * user choose one of the listed GA4 properties and starts the import, then returns to the import page.
 */
@Component({
  selector: 'ta-ga4-callback',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './ga4-callback.page.html'
})
export class Ga4CallbackPage implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private location = inject(Location);
  private importService = inject(ImportService);
  private toast = inject(ToastService);
  private entitlements = inject(ENTITLEMENTS);

  siteId = signal<string | null>(null);
  step = signal<'connecting' | 'choose' | 'starting' | 'error'>('connecting');
  error = signal<Ga4CallbackError | null>(null);
  errorDetail = signal('');
  properties = signal<Ga4Property[]>([]);
  selectedPropertyId = signal<string | null>(null);
  reconnecting = signal(false);
  private connectionId = '';

  ngOnInit() {
    const params = this.route.snapshot.queryParamMap;
    const code = params.get('code');
    const state = params.get('state');
    const googleError = params.get('error');
    this.siteId.set(sessionStorage.getItem(GA4_SITE_STORAGE_KEY));

    // The code is single-use, but it shouldn't stay in the address bar or the history either.
    this.location.replaceState(this.location.path().split('?')[0]);

    if (googleError) {
      this.fail(googleError === 'access_denied' ? 'cancelled' : 'other', googleError === 'access_denied' ? '' : 'Google returned an error.');
      return;
    }
    const siteId = this.siteId();
    if (!siteId || !code || !state) {
      this.fail('expired');
      return;
    }

    this.importService.connectGa4(siteId, code, state).subscribe({
      next: connection => {
        if (connection.properties.length === 0) {
          this.fail('noProperties');
          return;
        }
        this.connectionId = connection.connectionId;
        this.properties.set(connection.properties);
        this.selectedPropertyId.set(connection.properties[0].id);
        this.step.set('choose');
      },
      error: (err: HttpErrorResponse) => this.failFromHttp(err)
    });
  }

  startImport() {
    const siteId = this.siteId();
    const propertyId = this.selectedPropertyId();
    if (!siteId || !propertyId) return;

    this.step.set('starting');
    this.importService.startGa4Import(siteId, this.connectionId, propertyId).subscribe({
      next: () => {
        sessionStorage.removeItem(GA4_SITE_STORAGE_KEY);
        this.toast.success('Google Analytics import started');
        // The import page refreshes the job status every 5 s while it runs.
        this.router.navigate(['/account/sites', siteId, 'imports']);
      },
      error: (err: HttpErrorResponse) => this.failFromHttp(err)
    });
  }

  /** Every retry needs a new Google authorization: nothing from the previous one is kept. */
  reconnect() {
    const siteId = this.siteId();
    if (!siteId) {
      this.router.navigate(['/account/sites']);
      return;
    }
    this.reconnecting.set(true);
    this.importService.authorizeGa4(siteId).subscribe({
      next: ({ authorizationUrl }) => this.redirectTo(authorizationUrl),
      error: (err: HttpErrorResponse) => {
        this.toast.error(ga4AuthorizeErrorMessage(err, this.entitlements.planHint('import-denied') || undefined));
        this.reconnecting.set(false);
      }
    });
  }

  backToImports() {
    const siteId = this.siteId();
    this.router.navigate(siteId ? ['/account/sites', siteId, 'imports'] : ['/account/sites']);
  }

  /** Separate so tests can replace the navigation. */
  redirectTo(url: string) {
    window.location.assign(url);
  }

  private failFromHttp(err: HttpErrorResponse) {
    const detail = typeof err.error === 'string' ? err.error : (err.error?.error ?? '');
    switch (err.status) {
      case 400: this.fail('expired', detail); break;
      case 409: this.fail('conflict'); break;
      default: this.fail('other', ga4AuthorizeErrorMessage(err, this.entitlements.planHint('import-denied') || undefined));
    }
  }

  private fail(error: Ga4CallbackError, detail = '') {
    this.error.set(error);
    this.errorDetail.set(detail);
    this.step.set('error');
  }
}
