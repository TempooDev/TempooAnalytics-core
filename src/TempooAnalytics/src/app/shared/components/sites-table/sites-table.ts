import { DecimalPipe } from '@angular/common';
import { Component, inject, input, output, ChangeDetectionStrategy } from '@angular/core';
import { Router } from '@angular/router';

export interface SiteItem {
  id: string;
  siteKey: string;
  name: string;
  domain: string;
  pageviews: number;
  visitors: number;
  conversion: number;
  active: boolean;
  writeKey?: string;
}

@Component({
  selector: 'ta-sites-table',
  standalone: true,
  imports: [DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sites-table.html',
})
export class SitesTableComponent {
  sites = input<SiteItem[]>([]);
  delete = output<SiteItem>();
  router = inject(Router);

  open(site: SiteItem) {
    this.router.navigate(['/sites', site.siteKey]);
  }

  onDelete(event: Event, site: SiteItem) {
    event.stopPropagation();
    if (confirm(`Are you sure you want to delete ${site.name}?`)) {
      this.delete.emit(site);
    }
  }
}