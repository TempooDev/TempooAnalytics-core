import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { GA4_SITE_STORAGE_KEY, ImportService, ImportJobDto, ImportStatus, ImportType } from '../../../shared/services/import.service';
import { ToastService } from '../../../shared/services/toast.service';
import { FeatureFlagsService } from '../../../shared/services/feature-flags.service';
import { ENTITLEMENTS } from '@app/core/entitlements';
import { UPSELL_COMPONENT } from '@app/core/upsell';

/** While a job is pending or processing, the list is refreshed every few seconds. */
export const IMPORT_POLL_INTERVAL_MS = 5000;

/** Short explanation of a failed Google Analytics job, from the API's error message. */
export function ga4FailureTitle(errorMessage: string | undefined): string {
  const message = (errorMessage ?? '').toLowerCase();
  if (message.includes('quota')) return 'Google Analytics quota used up for today';
  if (message.includes('denied access') || message.includes('revoked')) return 'Google Analytics access was revoked';
  if (message.includes('nothing to import')) return 'Nothing to import';
  if (message.includes('interrupted')) return 'The download was interrupted';
  return 'The Google Analytics import failed';
}

/** Explains why the Google authorization could not start (403 plan or access, 409 active import, 503 not configured). */
export function ga4AuthorizeErrorMessage(err: HttpErrorResponse, deniedFallback = 'You are not allowed to import for this site.'): string {
  const detail = typeof err.error === 'string' ? err.error : (err.error?.error ?? err.error?.message);
  switch (err.status) {
    case 403: return detail || deniedFallback;
    case 409: return 'Another import is still running for this site. Wait until it finishes.';
    case 503: return 'Google Analytics import is not available right now. Try again later.';
    default: return 'Could not connect with Google: ' + (detail || 'Unknown error');
  }
}

@Component({
  selector: 'ta-import-management',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './import-management.page.html',
  styleUrl: './import-management.page.css'
})
export class ImportManagementPage implements OnInit {
  private route = inject(ActivatedRoute);
  private importService = inject(ImportService);
  private toast = inject(ToastService);
  private destroyRef = inject(DestroyRef);
  private flags = inject(FeatureFlagsService);

  siteId = signal<string | null>(null);
  imports = signal<ImportJobDto[]>([]);
  loading = signal(false);
  isImporting = signal(false);
  submitting = signal(false);

  ga4Enabled = this.flags.ga4Import;
  /** True when the server has no object storage (S3/R2): the upload cards are replaced by an explanation. */
  storageMissing = computed(() => !this.flags.dataStorage());
  private entitlements = inject(ENTITLEMENTS);
  upsell = inject(UPSELL_COMPONENT);
  isPaidPlan = computed(() => !this.entitlements.isFree());
  connectingGoogle = signal(false);
  /** A new import replaces the imported history, whichever tool it came from. */
  hasImportedHistory = computed(() => this.imports().some(j => j.status === ImportStatus.Completed));

  selectedFile = signal<File | null>(null);
  selectedFileName = signal('');

  private pollTimer: ReturnType<typeof setTimeout> | null = null;

  ImportStatus = ImportStatus;
  ImportType = ImportType;

  constructor() {
    this.destroyRef.onDestroy(() => this.stopPolling());
  }

  ngOnInit() {
    this.route.params.subscribe(params => {
      const id = params['siteId'];
      if (id) {
        this.siteId.set(id);
        this.loadImports();
      }
    });
  }

  loadImports(silent = false) {
    const id = this.siteId();
    if (!id) return;

    if (!silent) this.loading.set(true);
    this.importService.getImports(id).subscribe({
      next: (data) => {
        this.imports.set(data);
        const active = data.some(j => j.status === ImportStatus.Pending || j.status === ImportStatus.Processing);
        this.isImporting.set(active);
        this.loading.set(false);
        this.schedulePoll(active);
      },
      error: () => {
        if (!silent) this.toast.error('Failed to load imports');
        this.loading.set(false);
        this.stopPolling();
      }
    });
  }

  private schedulePoll(active: boolean) {
    this.stopPolling();
    if (!active) return;
    this.pollTimer = setTimeout(() => this.loadImports(true), IMPORT_POLL_INTERVAL_MS);
  }

  private stopPolling() {
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
  }

  onPlausibleFileSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files.length > 0) {
      const file = input.files[0];
      this.selectedFile.set(file);
      this.selectedFileName.set(file.name);
    }
  }

  startPlausibleImport() {
    const id = this.siteId();
    const file = this.selectedFile();
    if (!id || !file) return;

    this.submitting.set(true);
    this.importService.uploadPlausibleExport(id, file).subscribe({
      next: () => {
        this.toast.success('Plausible import started');
        this.submitting.set(false);
        this.selectedFile.set(null);
        this.selectedFileName.set('');
        this.loadImports();
      },
      error: (err: HttpErrorResponse) => {
        this.toast.error(this.uploadErrorMessage(err));
        this.submitting.set(false);
      }
    });
  }

  /** 400 and 409 return a plain string, 403 returns ErrorMessagePayload ({ error }). */
  uploadErrorMessage(err: HttpErrorResponse): string {
    const detail = typeof err.error === 'string' ? err.error : (err.error?.error ?? err.error?.message);
    switch (err.status) {
      case 400: return detail || 'The file must be a Plausible export .zip or .csv of up to 50 MB.';
      case 403: return detail || this.entitlements.planHint('import-denied') || 'You are not allowed to import for this site.';
      case 409: return 'Another import is still running for this site. Wait until it finishes.';
      case 413: return 'The file is too large (max 50 MB).';
      case 501: return 'La importación no está disponible en este servidor (sin almacenamiento S3 configurado).';
      default: return 'Failed to start Plausible import: ' + (detail || 'Unknown error');
    }
  }

  /**
   * Starts the Google authorization: the site is remembered for the callback route (it doesn't travel
   * through Google) and the browser goes to Google's consent screen.
   */
  connectGoogle() {
    const id = this.siteId();
    if (!id) return;

    this.connectingGoogle.set(true);
    this.importService.authorizeGa4(id).subscribe({
      next: ({ authorizationUrl }) => {
        sessionStorage.setItem(GA4_SITE_STORAGE_KEY, id);
        this.redirectTo(authorizationUrl);
      },
      error: (err: HttpErrorResponse) => {
        this.toast.error(ga4AuthorizeErrorMessage(err, this.entitlements.planHint('import-denied') || undefined));
        this.connectingGoogle.set(false);
      }
    });
  }

  /** Separate so tests can replace the navigation. */
  redirectTo(url: string) {
    window.location.assign(url);
  }

  ga4FailureTitle = ga4FailureTitle;

  canReconnect(job: ImportJobDto) {
    return this.ga4Enabled() && this.isPaidPlan() && !this.isImporting()
      && job.type === ImportType.GoogleAnalytics4 && job.status === ImportStatus.Failed;
  }

  getTypeLabel(type: ImportType) {
    switch (type) {
      case ImportType.GoogleAnalytics4: return 'GA4';
      case ImportType.Plausible: return 'Plausible';
      default: return 'CSV';
    }
  }

  /** Duration of a finished job, e.g. "1m 05s". */
  getDuration(job: ImportJobDto): string {
    if (!job.startedAt || !job.completedAt) return '—';
    const seconds = Math.max(0, Math.round((Date.parse(job.completedAt) - Date.parse(job.startedAt)) / 1000));
    const minutes = Math.floor(seconds / 60);
    return minutes > 0 ? `${minutes}m ${String(seconds % 60).padStart(2, '0')}s` : `${seconds}s`;
  }

  getStatusClass(status: ImportStatus) {
    switch (status) {
      case ImportStatus.Completed: return 'bg-emerald-500/10 text-emerald-500 border-emerald-500/20';
      case ImportStatus.Processing: return 'bg-sky-500/10 text-sky-500 border-sky-500/20 animate-pulse';
      case ImportStatus.Failed: return 'bg-red-500/10 text-red-500 border-red-500/20';
      case ImportStatus.Pending: return 'bg-slate-500/10 text-slate-500 border-slate-500/20';
      default: return 'bg-slate-500/10 text-slate-500 border-slate-500/20';
    }
  }

  getStatusLabel(status: ImportStatus) {
    switch (status) {
      case ImportStatus.Completed: return 'Completed';
      case ImportStatus.Processing: return 'Processing';
      case ImportStatus.Failed: return 'Failed';
      case ImportStatus.Pending: return 'Pending';
      case ImportStatus.Cancelled: return 'Cancelled';
      default: return 'Unknown';
    }
  }
}
