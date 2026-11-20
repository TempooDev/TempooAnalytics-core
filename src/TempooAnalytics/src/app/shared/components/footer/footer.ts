import { ChangeDetectionStrategy, Component } from '@angular/core';
import { environment } from '../../../../environments/environment';

/**
 * Page footer with the link to the source code. The core is published under the AGPL-3.0, whose section 13 asks that
 * users of a network service can get its source: the link points to `environment.sourceCodeUrl`. While the repository
 * is not published that value is empty and the footer shows nothing.
 */
@Component({
    selector: 'app-footer',
    standalone: true,
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        @if (sourceCodeUrl) {
            <footer class="mx-auto max-w-7xl px-6 pb-8 text-center text-xs text-slate-500">
                <a [href]="sourceCodeUrl" target="_blank" rel="noopener noreferrer" class="hover:text-slate-300 underline-offset-4 hover:underline">
                    Código fuente
                </a>
            </footer>
        }
    `,
})
export class FooterComponent {
    /** Public repository URL, or empty when there is none yet. Overridable in tests. */
    sourceCodeUrl: string = environment.sourceCodeUrl;
}
