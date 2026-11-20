import { Component, model, input, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AnalyticsBreakdown } from '../../../services/analytics.service';

@Component({
  selector: 'ta-breakdown-selector',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="flex items-center bg-slate-950 border border-slate-800 rounded-lg p-1">
      @for (option of options; track option.value) {
        <button (click)="value.set(option.value)"
            [class.bg-slate-800]="value() === option.value"
            [class.text-sky-400]="value() === option.value"
            class="px-3 py-1 text-[10px] font-bold rounded-md transition-all hover:text-sky-400 uppercase">
          {{ option.label }}
        </button>
      }
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BreakdownSelectorComponent {
  value = model.required<AnalyticsBreakdown>();
  
  options: { label: string, value: AnalyticsBreakdown }[] = [
    { label: 'Hour', value: 'hour' },
    { label: 'Day', value: 'day' },
    { label: 'Week', value: 'week' },
    { label: 'Month', value: 'month' }
  ];
}
