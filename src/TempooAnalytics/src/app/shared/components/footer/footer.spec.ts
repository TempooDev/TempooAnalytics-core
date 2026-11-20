import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FooterComponent } from './footer';

describe('FooterComponent', () => {
    beforeEach(() => TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] }));
    afterEach(() => TestBed.resetTestingModule());

    async function render(url: string): Promise<HTMLElement> {
        const fixture = TestBed.createComponent(FooterComponent);
        fixture.componentInstance.sourceCodeUrl = url;
        await fixture.whenStable();
        return fixture.nativeElement as HTMLElement;
    }

    it('links to the source code when the repository is published', async () => {
        const link = (await render('https://github.com/example/core')).querySelector('a');

        expect(link?.getAttribute('href')).toBe('https://github.com/example/core');
        expect(link?.textContent?.trim()).toBe('Código fuente');
        expect(link?.getAttribute('rel')).toContain('noopener');
    });

    it('shows nothing while there is no repository URL', async () => {
        const element = await render('');

        expect(element.querySelector('a')).toBeNull();
        expect(element.querySelector('footer')).toBeNull();
    });
});
