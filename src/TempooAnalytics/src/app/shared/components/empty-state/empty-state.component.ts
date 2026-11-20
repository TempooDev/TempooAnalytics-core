import { Component, Input, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';

@Component({
    selector: 'ta-empty-state',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './empty-state.component.html',
})
export class EmptyStateComponent {
    @Input() title: string = 'Welcome to Tempoo Analytics!';
    @Input() message: string = "It looks like you haven't set up any sites or received any events yet. Check our documentation to learn how to integrate the tracker in seconds.";

    router = inject(Router);

    goToDocs() {
        this.router.navigate(['/docs']);
    }
}
