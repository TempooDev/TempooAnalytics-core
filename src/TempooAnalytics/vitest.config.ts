import { defineConfig } from 'vitest/config';
import tsconfigPaths from 'vite-tsconfig-paths';

export default defineConfig({
    plugins: [tsconfigPaths()],
    test: {
        globals: true,
        environment: 'jsdom',
        include: ['src/**/*.spec.ts'],
        setupFiles: 'src/test-setup.ts',
        threads: false,
        deps: {
            // Inline Angular packages so Vite transforms them and testing subpaths resolve
            inline: [/^@angular\//, /^@nguniversal\//]
        },
    },
    optimizeDeps: {
        include: ['@angular/platform-browser-dynamic/testing', '@angular/platform-browser-dynamic']
    }
});
