import { Component, input, ChangeDetectionStrategy, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LocationMapComponent } from '@app/shared/components/location-map/location-map';

@Component({
  selector: 'ta-top-locations',
  standalone: true,
  imports: [CommonModule, LocationMapComponent],
  templateUrl: './top-locations.html',
  styleUrls: [],
  styles: [`:host { display: block; height: 100%; }`],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TopLocationsComponent {
  mapData = input<any[]>([]);
  table = input<{ country: string; countryCode: string; visitors: number; percent: number }[]>([]);

  activeTab = signal<'list' | 'map'>('list');

  getFlagEmoji(countryCode: string): string {
    if (!countryCode || countryCode === 'unknown') return '🏳️';
    const codePoints = countryCode
      .toUpperCase()
      .split('')
      .map(char => 127397 + char.charCodeAt(0));
    return String.fromCodePoint(...codePoints);
  }
}