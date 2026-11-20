/**
 * The community build is the core without src/app/cloud. This guard makes the rule visible in the test run:
 * nothing outside `cloud/` may import from it (the one place that wires it is the cloud entry point, outside app/).
 */
const sources = import.meta.glob('/src/app/**/*.ts', { query: '?raw', import: 'default', eager: true }) as Record<string, string>;

const importPattern = /(?:from|import)\s*\(?\s*['"]([^'"]+)['"]/g;

function importsOf(code: string): string[] {
    return [...code.matchAll(importPattern)].map(m => m[1]);
}

describe('core does not depend on cloud', () => {
    const outside = Object.entries(sources).filter(([path]) => !path.startsWith('/src/app/cloud/'));

    it('scans the core sources', () => {
        expect(outside.length).toBeGreaterThan(50);
        // app/cloud is not in the public repository, so its presence is not asserted here (the community build excludes it).
    });

    it('has no import of app/cloud outside app/cloud', () => {
        const offenders = outside.flatMap(([path, code]) =>
            importsOf(code)
                .filter(spec => /(^|\/)cloud(\/|$)/.test(spec))
                .map(spec => `${path} imports ${spec}`));
        expect(offenders).toEqual([]);
    });

    it('detects an offending import (self-test of the matcher)', () => {
        const specs = importsOf("import { X } from '@app/cloud/services/ai.service';\nconst m = await import('./cloud/a');");
        expect(specs.filter(s => /(^|\/)cloud(\/|$)/.test(s))).toHaveLength(2);
    });
});
