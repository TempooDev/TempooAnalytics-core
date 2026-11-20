import { Component, Input, Output, EventEmitter, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormArray, Validators } from '@angular/forms';
import { FunnelService, FunnelResult } from '../../../services/funnel.service';
import { FunnelDefinition, FunnelStepType } from '../../../services/analytics.service';
import { firstValueFrom } from 'rxjs';

@Component({
    selector: 'app-funnel-builder',
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule],
    templateUrl: './funnel-builder.component.html'
})
export class FunnelBuilderComponent {
    @Input() siteId!: string;
    @Input() initialDefinition?: FunnelDefinition;
    @Output() calculateResult = new EventEmitter<FunnelResult>();

    private fb = inject(FormBuilder);
    private funnelService = inject(FunnelService);

    // 1. Metadata Resource
    metadataResource = this.funnelService.getEventMetadataResource(() => ({ siteId: this.siteId }));
    metadata = computed(() => this.metadataResource.value());
    loadingMetadata = computed(() => this.metadataResource.isLoading());

    form = this.fb.group({
        name: ['New Funnel', Validators.required],
        steps: this.fb.array([])
    });

    loading = signal(false);
    stepTypes: FunnelStepType[] = ['Pageview', 'CustomEvent'];

    get steps() {
        return this.form.get('steps') as FormArray;
    }

    ngOnInit() {
        if (this.initialDefinition) {
            this.form.patchValue({ name: this.initialDefinition.name });
            this.steps.clear();
            this.initialDefinition.steps.forEach((s: any) => {
                this.steps.push(this.fb.group({
                    name: [s.name, Validators.required],
                    type: [s.type, Validators.required],
                    value: [s.value, Validators.required]
                }));
            });
        } else {
            // Add default steps for new funnel
            this.addStep();
            this.addStep();
        }
    }

    addStep() {
        const stepGroup = this.fb.group({
            name: [`Step ${this.steps.length + 1}`, Validators.required],
            type: ['Pageview' as FunnelStepType, Validators.required],
            value: ['', Validators.required]
        });
        this.steps.push(stepGroup);
    }

    removeStep(index: number) {
        this.steps.removeAt(index);
    }

    async calculate() {
        if (this.form.invalid) return;
        this.loading.set(true);

        const formVal = this.form.value;
        const def: FunnelDefinition = {
            siteId: this.siteId,
            name: formVal.name || 'Funnel',
            steps: (formVal.steps || []).map((s: any, i: number) => ({
                order: i + 1,
                type: s.type,
                value: s.value,
                name: s.name
            }))
        };

        try {
            const res = await firstValueFrom(this.funnelService.calculateFunnel(def));
            this.calculateResult.emit(res);
        } catch (err) {
            // Error handled
        } finally {
            this.loading.set(false);
        }
    }

    async save() {
        if (this.form.invalid) return;
        this.loading.set(true);

        const formVal = this.form.value;
        const def: FunnelDefinition = {
            siteId: this.siteId,
            name: formVal.name || 'New Funnel',
            steps: (formVal.steps || []).map((s: any, i: number) => ({
                order: i + 1,
                type: s.type,
                value: s.value,
                name: s.name
            }))
        };

        try {
            if (this.initialDefinition?.id) {
                await firstValueFrom(this.funnelService.updateFunnel(this.initialDefinition.id, def));
            } else {
                await firstValueFrom(this.funnelService.createFunnel(def));
            }
            // trigger reload in parent
            this.calculateResult.emit({ steps: [], overallConversionRate: 0 }); 
        } catch (err) {
            // Error handled
        } finally {
            this.loading.set(false);
        }
    }
}
