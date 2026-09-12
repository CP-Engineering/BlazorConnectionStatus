import { defineConfig } from 'vitest/config';

export default defineConfig({
    test: {
        // jsdom supplies window, navigator and the event target the module listens on.
        environment: 'jsdom',
        include: ['tests/js/**/*.test.js'],
    },
});
