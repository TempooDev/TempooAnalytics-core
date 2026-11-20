import { Component, input, OnInit, signal, inject, Output, EventEmitter, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AnalyticsService, EventDetailDto, AnalyticsFilterDto, AdvancedFilter } from '@app/shared/services';
import { firstValueFrom } from 'rxjs';
import { DatePicker } from 'primeng/datepicker';
import { Tooltip } from 'primeng/tooltip';

@Component({
  selector: 'app-events-modal',
  standalone: true,
  imports: [CommonModule, FormsModule, DatePicker, Tooltip],
  templateUrl: './events-modal.component.html',
  styles: [`
    :host ::ng-deep .tempoo-datepicker-modal {
        width: 100%;
        flex: 1;
    }
    :host ::ng-deep .tempoo-datepicker-modal .p-datepicker-input,
    :host ::ng-deep .tempoo-datepicker-modal .p-inputtext {
        background: transparent !important;
        border: none !important;
        color: #e2e8f0 !important;
        font-size: 0.75rem !important;
        padding: 0 !important;
        cursor: pointer;
        box-shadow: none !important;
        width: 100%;
    }
  `]
})
export class EventsModalComponent implements OnInit {
  siteKey = input.required<string>();
  days = input(30);
  customFrom = input<string | null>(null);
  customTo = input<string | null>(null);
  filters = input<AnalyticsFilterDto | undefined>();
  initialEventName = input<string | undefined>();

  @Output() close = new EventEmitter<void>();

  private analytics = inject(AnalyticsService);

  events = signal<EventDetailDto[]>([]);
  loading = signal<boolean>(false);

  // Filtering Internal State
  internalDateRange = signal<Date[]>([]);
  activeFilters = signal<{ property: string, operator: string, values: string[], newValue?: string }[]>([]);
  showFilterBuilder = signal<boolean>(false);

  availableProperties = [
      { label: 'Event', value: 'event name' },
      { label: 'User Alias', value: 'user_alias' },
      { label: 'User Type', value: 'traffic type' },
      { label: 'URL', value: 'path' },
      { label: 'Page Title', value: 'page title' },
      { label: 'Country', value: 'country' },
      { label: 'City', value: 'city' },
      { label: 'Browser', value: 'browser' },
      { label: 'OS', value: 'os' },
      { label: 'Device', value: 'device' },
      { label: 'User ID', value: 'user id' }
  ];

  availableOperators = [
      { label: 'Is', value: 'is' },
      { label: 'Is not', value: 'is_not' },
      { label: 'Contains', value: 'contains' },
      { label: 'Not contains', value: 'not_contains' },
      { label: 'Matches regex', value: 'matches' },
      { label: 'Not matches regex', value: 'not_matches' }
  ];

  getFilterLabel(val: string): string { return this.availableProperties.find(p => p.value === val)?.label || val; }
  getOperatorLabel(val: string): string { return this.availableOperators.find(o => o.value === val)?.label || val; }

  addFilter() { 
    this.activeFilters.update(f => [...f, { property: 'event name', operator: 'contains', values: [], newValue: '' }]); 
    this.showFilterBuilder.set(true);
  }
  removeFilter(index: number) { 
    this.activeFilters.update(f => f.filter((_, i) => i !== index)); 
    if (this.activeFilters().length === 0) this.showFilterBuilder.set(false);
    this.onFilterChange(); 
  }
  removeFilterTag(filterIndex: number, tagIndex: number) {
      this.activeFilters.update(filters => {
          const updated = [...filters];
          updated[filterIndex] = { ...updated[filterIndex], values: updated[filterIndex].values.filter((_: any, i: number) => i !== tagIndex) };
          return updated;
      });
      this.onFilterChange();
  }
  updateFilterValue(index: number, value: string) {
      if (!value) return;
      this.activeFilters.update(filters => {
          const updated = [...filters];
          if (!updated[index].values.includes(value)) {
              updated[index] = { ...updated[index], values: [...updated[index].values, value], newValue: '' };
          }
          return updated;
      });
      this.onFilterChange();
  }
  updateFilterProperty(index: number, prop: string) { this.activeFilters.update(filters => { const updated = [...filters]; updated[index] = { ...updated[index], property: prop }; return updated; }); this.onFilterChange(); }
  updateFilterOperator(index: number, op: string) { this.activeFilters.update(filters => { const updated = [...filters]; updated[index] = { ...updated[index], operator: op }; return updated; }); this.onFilterChange(); }

  filterByUser(event: EventDetailDto) {
    // If a filter for this user already exists, don't add it again
    const exists = this.activeFilters().some(f => f.property === 'user id' && f.values.includes(event.visitorId));
    if (exists) return;

    this.activeFilters.update(f => [...f, { 
        property: 'user id', 
        operator: 'is', 
        values: [event.visitorId], 
        newValue: '' 
    }]);
    this.onFilterChange();
  }

  // Pagination
  limit = signal<number>(50);
  offset = signal<number>(0);
  hasMore = signal<boolean>(true);
  maxDate = new Date();

  ngOnInit() {
    // Initialize date range from inputs
    if (this.customFrom()) {
      const from = new Date(this.customFrom()!);
      const to = this.customTo() ? new Date(this.customTo()!) : new Date();
      this.internalDateRange.set([from, to]);
    } else {
      const to = new Date();
      const from = new Date();
      from.setDate(to.getDate() - this.days());
      this.internalDateRange.set([from, to]);
    }

    if (this.initialEventName()) {
        if (this.initialEventName() === '__custom_only__') {
            this.activeFilters.set([{
                property: 'event name',
                operator: 'is_not',
                values: ['pageview'],
                newValue: ''
            }]);
        } else {
            this.activeFilters.set([{
                property: 'event name',
                operator: 'is',
                values: [this.initialEventName()!],
                newValue: ''
            }]);
        }
    }

    this.loadEvents();
  }

  onFilterChange() {
    this.offset.set(0);
    this.loadEvents();
  }

  async loadEvents(append: boolean = false) {
    if (this.loading()) return;

    this.loading.set(true);
    try {
      const range = this.internalDateRange();
      const from = range && range[0] ? this.getLocalISOString(range[0]) : undefined;
      const to = range && range[1] ? this.getLocalISOString(range[1]) : undefined;

      // Merge dashboard filters with local modal filters
      const mergedFilters: AnalyticsFilterDto = {
        advancedFilters: [
            ...(this.filters()?.advancedFilters || []),
            ...this.activeFilters().filter(f => f.values && f.values.length > 0)
        ]
      };

      const data = await firstValueFrom(this.analytics.getEventsDetails(
        this.days(),
        this.siteKey(),
        mergedFilters,
        from,
        to,
        this.limit(),
        this.offset()
      ));

      if (append) {
        this.events.update(e => [...e, ...data]);
      } else {
        this.events.set(data);
      }

      this.hasMore.set(data.length === this.limit());
    } catch (e) {
      console.error('Failed to load events', e);
    } finally {
      this.loading.set(false);
    }
  }

  private getLocalISOString(date: Date): string {
    const offset = date.getTimezoneOffset();
    const localDate = new Date(date.getTime() - (offset * 60 * 1000));
    return localDate.toISOString().slice(0, 16);
  }

  loadMore() {
    this.offset.update(o => o + this.limit());
    this.loadEvents(true);
  }

  getTechIcon(type: 'browser' | 'os' | 'device', value: string): string {
    if (!value) return 'pi pi-question';
    const val = value.toLowerCase();
    
    if (type === 'browser') {
        if (val.includes('chrome')) return 'pi pi-google';
        if (val.includes('firefox')) return 'pi pi-compass'; // Better fallback for browser
        if (val.includes('safari')) return 'pi pi-compass';
        if (val.includes('edge')) return 'pi pi-microsoft';
        if (val.includes('brave')) return 'pi pi-shield';
        if (val.includes('opera')) return 'pi pi-circle';
        if (val.includes('vivaldi')) return 'pi pi-vimeo'; // Closest visual V icon
        return 'pi pi-globe';
    } else if (type === 'os') {
        if (val.includes('windows')) return 'pi pi-microsoft';
        if (val.includes('mac') || val.includes('ios')) return 'pi pi-apple';
        if (val.includes('android')) return 'pi pi-android';
        if (val.includes('linux')) return 'pi pi-server';
        return 'pi pi-desktop';
    } else {
        if (val.includes('mobile')) return 'pi pi-mobile';
        if (val.includes('tablet')) return 'pi pi-tablet';
        return 'pi pi-desktop';
    }
  }
}
