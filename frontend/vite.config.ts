import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

// The frontend (ADR 0038). In development the app calls its own origin and Vite forwards /api to the API, as the proxy
// of Prompt 8.1 will in production; BILLINGRECON_API_URL points it elsewhere than the API's default address.
export default defineConfig({
  plugins: [react()],
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
