import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class RangeStore {
    // internal signal holding the number of days for the range
    private readonly _range = signal<number>(30);

    // exposed readonly signal for consumers
    readonly range = this._range;

    setRange(days: number) {
        if (typeof days === 'number' && days > 0) {
            this._range.set(days);
        }
    }
}
