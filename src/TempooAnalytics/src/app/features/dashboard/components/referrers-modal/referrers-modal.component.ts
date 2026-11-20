import { Component, Input, OnInit, signal, computed, inject, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AnalyticsService, ReferrerDetail, AnalyticsFilterDto } from '@app/shared/services';

@Component({
  selector: 'app-referrers-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './referrers-modal.component.html'
})
export class ReferrersModalComponent implements OnInit {
  @Input({ required: true }) siteKey!: string;
  @Input({ required: true }) days!: number;
  @Input() from?: string | null;
  @Input() to?: string | null;
  @Input() filters?: any; // Using any for compatibility with the page signal
  @Input() initialViewMode: 'referrers' | 'channels' = 'referrers';
  
  @Output() close = new EventEmitter<void>();

  private analytics = inject(AnalyticsService);
  
  viewMode = signal<'referrers' | 'channels'>('referrers');
  referrers = signal<ReferrerDetail[]>([]);
  isLoading = signal(true);
  searchQuery = signal('');
  sortColumn = signal<keyof ReferrerDetail>('sessions');
  sortDirection = signal<'asc' | 'desc'>('desc');

  filteredReferrers = computed(() => {
    let list = this.referrers().filter(r => 
      (r.referrer || 'Direct').toLowerCase().includes(this.searchQuery().toLowerCase())
    );

    const col = this.sortColumn();
    const dir = this.sortDirection() === 'asc' ? 1 : -1;

    return [...list].sort((a, b) => {
      const valA = a[col];
      const valB = b[col];
      if (typeof valA === 'string' && typeof valB === 'string') {
        return valA.localeCompare(valB) * dir;
      }
      return (((valA as number) || 0) - ((valB as number) || 0)) * dir;
    });
  });

  ngOnInit() {
    this.viewMode.set(this.initialViewMode);
    this.loadData();
  }

  loadData() {
    this.isLoading.set(true);
    const effectiveSiteKey = this.siteKey && this.siteKey !== '' ? this.siteKey : undefined;
    
    let cleanFilters: AnalyticsFilterDto | undefined = undefined;
    if (this.filters && this.filters.length > 0) {
        cleanFilters = {
            advancedFilters: this.filters
                .filter((f: any) => f.values && f.values.length > 0)
                .map(({ property, operator, values }: any) => ({ property, operator, values }))
        };
    }

    const request = this.viewMode() === 'referrers' 
        ? this.analytics.getReferrerDetails(this.days, effectiveSiteKey, cleanFilters, this.from || undefined, this.to || undefined)
        : this.analytics.getChannelDetails(this.days, effectiveSiteKey, cleanFilters, this.from || undefined, this.to || undefined);

    request.subscribe({
      next: (data) => {
        this.referrers.set(data);
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false)
    });
  }

  switchView(mode: 'referrers' | 'channels') {
    this.viewMode.set(mode);
    this.loadData();
  }

  getChannelIcon(channel: string): string {
    switch (channel.toLowerCase()) {
      case 'direct': return 'pi pi-directions';
      case 'organic search': return 'pi pi-search';
      case 'organic social': return 'pi pi-share-alt';
      case 'ai tools': return 'pi pi-sparkles';
      case 'referral': return 'pi pi-link';
      default: return 'pi pi-question-circle';
    }
  }

  toggleSort(column: keyof ReferrerDetail) {
    if (this.sortColumn() === column) {
      this.sortDirection.update(d => d === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortColumn.set(column);
      this.sortDirection.set('desc');
    }
  }
}
