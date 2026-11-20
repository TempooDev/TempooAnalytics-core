import { Component, input, computed, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { Device } from '@app/shared/services';

@Component({
    selector: 'ta-top-devices',
    standalone: true,
    imports: [CommonModule, DecimalPipe],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './top-devices.html'
})
export class TopDevicesComponent {
    devices = input<Device[]>([]);

    processedDevices = computed(() => {
        const data = this.devices() || [];
        const total = data.reduce((acc, curr) => acc + curr.value, 0) || 1;

        // Sort by value desc
        return [...data]
            .sort((a, b) => b.value - a.value)
            .map(d => ({
                type: d.deviceType,
                value: d.value,
                percent: Math.round((d.value / total) * 100)
            }));
    });

    getColor(type: string): string {
        switch (type.toLowerCase()) {
            case 'desktop': return 'bg-indigo-500';
            case 'mobile': return 'bg-pink-500';
            case 'tablet': return 'bg-purple-500';
            default: return 'bg-slate-500';
        }
    }
}
