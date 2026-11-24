import { defineConfig, devices } from '@playwright/test';

// The browser suite of the main journey (Prompt 8.2, ADR 0046): run against a whole running stack, by default the
// one-command local run with the demo hospital at https://localhost, whose certificate is the local one nginx made
// (docs/runbook.md section 14). E2E_BASE_URL points it elsewhere, for example the Vite dev server; E2E_BROWSER_CHANNEL
// uses an installed browser ("msedge", "chrome") instead of the Chromium of `npx playwright install chromium`.
// The journey is one story told in order, so one worker, no retry: a step that fails is the finding.
const channel = process.env.E2E_BROWSER_CHANNEL;

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'https://localhost',
    ignoreHTTPSErrors: true,
    trace: 'retain-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'], ...(channel ? { channel } : {}) },
    },
  ],
});
