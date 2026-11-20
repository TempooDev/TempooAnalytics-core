import { Component, input } from '@angular/core';

@Component({
  selector: 'ta-metric-card',
  standalone: true,
  templateUrl: './metric-card.html',
})
export class MetricCardComponent {
  label = input<string>('');
  value = input<string | number>('');
  change = input<string | null>(null);
  changeLabel = input<string | null>(null);
  positive = input<boolean>(true);
}
