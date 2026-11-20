import { Component, input, output, computed, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
    selector: 'ta-range-selector',
    standalone: true,
    imports: [CommonModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './range-selector.html',
})
export class RangeSelector {
    // Bounds can be configured by the parent
    public readonly min = input<number>(0);
    public readonly max = input<number>(100);

    // Current value is provided by parent as an input
    public readonly value = input<number>(30);

    // Emit when the selected range changes
    public readonly valueChange = output<number>();

    setValue(newValue: string | number) {
        const days = typeof newValue === 'string' ? parseInt(newValue, 10) : newValue;
        if (days >= this.min() && days <= this.max()) {
            this.valueChange.emit(days);
        }
    }

    // Human-readable labels for common ranges
    private readonly ranges = [
        { days: 7, label: 'Last 7 days' },
        { days: 30, label: 'Last 30 days' },
        { days: 90, label: 'Last 90 days' },
    ];

    selectedLabel = computed(() => {
        const found = this.ranges.find(r => r.days === this.value());
        return found ? found.label : `${this.value()} days`;
    });

    onSelectChange(event: Event) {
        const el = event.target as HTMLSelectElement | null;
        if (!el) return;
        const val = parseInt(el.value, 10);
        if (!isNaN(val)) this.setValue(val);
    }
}