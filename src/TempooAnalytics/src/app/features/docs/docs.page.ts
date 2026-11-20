import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { APP_SETTINGS, apiUrlAttribute } from '@app/core/app-settings';

@Component({
    selector: 'app-docs',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './docs.page.html',
    styles: [`
    :host {
      display: block;
      padding: 2rem 0;
    }
  `]
})
export class DocsPage {
    private readonly settings = inject(APP_SETTINGS);
    readonly scriptUrl = this.settings.trackingScriptUrl;
    /** ` data-api-url="..."` for the one-line snippets; empty when the settings carry no API URL. */
    readonly apiAttr = apiUrlAttribute(this.settings);
    /** Extra line of the Streamlit example that sets the API URL; empty when there is none. */
    readonly apiScriptLine = this.settings.trackingApiUrl
        ? `\n            script.setAttribute('data-api-url', '${this.settings.trackingApiUrl}');`
        : '';
    readonly supportEmail = this.settings.supportEmail;
}
