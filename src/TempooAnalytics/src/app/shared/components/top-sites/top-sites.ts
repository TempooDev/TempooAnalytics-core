import { DecimalPipe } from '@angular/common';
import { Component, input } from '@angular/core';

export interface TopSite {
  name: string;
  domain?: string;
  views: number;
  change: number;
}

@Component({
  selector: 'ta-top-sites',
  standalone: true,
  imports: [DecimalPipe],
  templateUrl: './top-sites.html',
})
export class TopSitesComponent {
  sites = input<TopSite[]>([]);
  title = input<string>('Top sites');
}
