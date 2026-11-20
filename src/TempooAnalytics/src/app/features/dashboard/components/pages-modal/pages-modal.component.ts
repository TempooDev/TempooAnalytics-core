import { Component, Input, OnInit, signal, computed, inject, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AnalyticsService, PageDetail, AnalyticsFilterDto } from '@app/shared/services';

@Component({
  selector: 'app-pages-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './pages-modal.component.html'
})
export class PagesModalComponent implements OnInit {
  @Input({ required: true }) siteKey!: string;
  @Input({ required: true }) days!: number;
  @Input() from?: string | null;
  @Input() to?: string | null;
  @Input() filters?: any;
  @Input() initialViewMode: 'pages' | 'titles' | 'entry' | 'exit' = 'pages';

  @Output() close = new EventEmitter<void>();

  private analytics = inject(AnalyticsService);
  
  viewMode = signal<'pages' | 'titles' | 'entry' | 'exit'>('pages');
  pages = signal<PageDetail[]>([]);
  isLoading = signal(true);
  searchQuery = signal('');
  sortColumn = signal<keyof PageDetail>('views');
  sortDirection = signal<'asc' | 'desc'>('desc');

  filteredPages = computed(() => {
    let list = this.pages().filter(p => 
      (p.page || '').toLowerCase().includes(this.searchQuery().toLowerCase())
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

    this.analytics.getPageDetails(this.viewMode(), this.days, effectiveSiteKey, cleanFilters, this.from || undefined, this.to || undefined).subscribe({
      next: (data) => {
        this.pages.set(data);
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false)
    });
  }

  switchView(mode: 'pages' | 'titles' | 'entry' | 'exit') {
    this.viewMode.set(mode);
    this.loadData();
  }

  toggleSort(column: keyof PageDetail) {
    if (this.sortColumn() === column) {
      this.sortDirection.update(d => d === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortColumn.set(column);
      this.sortDirection.set('desc');
    }
  }

  formatDuration(seconds: number): string {
    if (!seconds) return '0s';
    if (seconds < 1) return '< 1s';
    const mins = Math.floor(seconds / 60);
    const secs = Math.round(seconds % 60);
    return mins > 0 ? `${mins}m ${secs}s` : `${secs}s`;
  }
}
