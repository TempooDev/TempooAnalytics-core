import { Injectable, ApplicationRef, Injector, createComponent, EnvironmentInjector } from '@angular/core';
import { ToastComponent, ToastType } from '@app/shared/components/toast/toast.component';
import { first } from 'rxjs/operators';

@Injectable({ providedIn: 'root' })
export class ToastService {
    private queue: Array<{ message: string, type: ToastType, duration?: number }> = [];

    constructor(
        private injector: Injector, 
        private appRef: ApplicationRef, 
        private environmentInjector: EnvironmentInjector
    ) { 
        // Watch for stability to process any queued messages from APP_INITIALIZER
        this.appRef.isStable.pipe(
            first(stable => stable)
        ).subscribe(() => {
            this.processQueue();
        });
    }

    private processQueue() {
        while (this.queue.length > 0) {
            const item = this.queue.shift();
            if (item) this.show(item.message, item.type, item.duration);
        }
    }

    show(message: string, type: ToastType = 'info', duration = 4000) {
        // If app is not yet stable, queue the message
        if (!this.appRef.components.length) {
            this.queue.push({ message, type, duration });
            return;
        }

        try {
            const compRef = createComponent(ToastComponent, {
                environmentInjector: this.environmentInjector,
                elementInjector: this.injector
            });

            this.appRef.attachView(compRef.hostView);
            compRef.instance.message = message;
            compRef.instance.type = type;

            compRef.changeDetectorRef.detectChanges();

            const el = compRef.location.nativeElement as HTMLElement;
            document.body.appendChild(el);

            // Animate in
            requestAnimationFrame(() => {
                const body = el.querySelector('.toast-body') as HTMLElement | null;
                if (body) {
                    body.style.opacity = '1';
                    body.style.transform = 'translateY(0)';
                }
            });

            // Cleanup after duration
            setTimeout(() => {
                const body = el.querySelector('.toast-body') as HTMLElement | null;
                if (body) {
                    body.style.opacity = '0';
                    body.style.transform = 'translateY(10px)';
                    setTimeout(() => {
                        this.appRef.detachView(compRef.hostView);
                        compRef.destroy();
                    }, 400);
                } else {
                    this.appRef.detachView(compRef.hostView);
                    compRef.destroy();
                }
            }, duration);
        } catch (err) {
            console.error('Toast show failed', err);
        }
    }

    success(message: string, duration?: number) {
        this.show(message, 'success', duration);
    }

    error(message: string, duration?: number) {
        this.show(message, 'error', duration);
    }

    warning(message: string, duration?: number) {
        this.show(message, 'warning', duration);
    }

    info(message: string, duration?: number) {
        this.show(message, 'info', duration);
    }
}
