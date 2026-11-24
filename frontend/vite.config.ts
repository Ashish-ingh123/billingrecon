import react from '@vitejs/plugin-react';
import type { Plugin } from 'vite';
import { defineConfig } from 'vitest/config';

/**
 * Preloads the Latin face of Inter in the built page (Prompt 9.4b, "The bar of Phase 9", point 5): the typeface arrives
 * with the first paint instead of replacing its fallback a moment later, which reflowed text on a narrow window. The
 * file's hashed name is known only once the bundle is written, so the link is added to index.html then.
 */
function preloadTypeface(): Plugin {
  return {
    name: 'billingrecon-preload-typeface',
    apply: 'build',
    transformIndexHtml: {
      order: 'post',
      handler(_html, context) {
        const face = Object.keys(context.bundle ?? {}).find((name) =>
          /inter-latin-wght-normal-[\w-]+\.woff2$/.test(name),
        );
        return face === undefined
          ? []
          : [
              {
                tag: 'link',
                attrs: { rel: 'preload', as: 'font', type: 'font/woff2', href: `/${face}`, crossorigin: '' },
                injectTo: 'head',
              },
            ];
      },
    },
  };
}

// The frontend (ADR 0038). In development the app calls its own origin and Vite forwards /api to the API, as the proxy
// of Prompt 8.1 will in production; BILLINGRECON_API_URL points it elsewhere than the API's default address.
export default defineConfig({
  plugins: [react(), preloadTypeface()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': { target: process.env.BILLINGRECON_API_URL ?? 'http://localhost:5080' },
    },
  },
  test: {
    // The component tests; the browser journey in e2e/ is Playwright's (ADR 0046).
    include: ['src/**/*.test.{ts,tsx}'],
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    restoreMocks: true,
    unstubGlobals: true,
  },
});
