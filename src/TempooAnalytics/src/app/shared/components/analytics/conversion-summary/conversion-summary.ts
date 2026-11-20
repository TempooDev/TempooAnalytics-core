import { Component, input, computed, inject } from '@angular/core';
import { CommonModule, DecimalPipe, PercentPipe } from '@angular/common';
import { UPSELL_COMPONENT } from '@app/core/upsell';

@Component({
  selector: 'ta-conversion-summary',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './conversion-summary.html',
})
export class ConversionSummaryComponent {
  data = input<{ label: string; value: number }[]>([]);
  title = input<string>('Conversion Funnel');
  mode = input<'bar' | 'funnel'>('bar');
  locked = input<boolean>(false);
  upsell = inject(UPSELL_COMPONENT);

  maxValue = computed(() => {
    const values = this.data().map(d => d.value);
    const max = values.length ? Math.max(...values) : 0;
    return max > 0 ? max : 1;
  });

  // Colors for funnel steps
  stepColors = ['#f59e0b', '#10b981', '#3b82f6', '#8b5cf6', '#ec4899'];

  getStepColor(index: number): string {
    return this.stepColors[index % this.stepColors.length];
  }
}