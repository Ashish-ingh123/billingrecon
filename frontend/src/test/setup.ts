import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach, beforeEach } from 'vitest';
import { stubColourScheme } from './colourScheme';

// jsdom has no matchMedia: every test starts with a system that prefers light, which a test may change.
beforeEach(() => {
  stubColourScheme(false);
  window.localStorage.clear();
  window.sessionStorage.clear();
  document.documentElement.removeAttribute('data-theme');
  document.documentElement.removeAttribute('data-theme-setting');
});

afterEach(() => {
  cleanup();
});
