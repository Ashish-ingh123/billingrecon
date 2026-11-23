import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it, vi } from 'vitest';
import { darkSchemeQuery, themeStorageKey } from '../theme/theme';
import { stubColourScheme } from './colourScheme';

/**
 * The inline script of index.html sets the theme before React mounts, so a hard refresh never shows a frame of the
 * wrong theme (ADR 0038, point 7). These tests run it as the browser does, hold it to the provider's key and query, and
 * pin its SHA-256, which the content security policy of Prompt 8.1 lists as `script-src 'sha256-...'`.
 */
const indexHtml = readFileSync(join(process.cwd(), 'index.html'), 'utf8');
const inline = /<script>([\s\S]*?)<\/script>/.exec(indexHtml);
const script = inline?.[1] ?? '';

/** The hash the content security policy lists. Changing the script changes it: update it here, and in the policy. */
const pinnedSha256 = 'EiUJJFQJQCBNqB/aazghpxtqLJ7StNqlkar3PJPRG+g=';

function runScript(): void {
  // The browser runs the script as it parses the page; this runs the same text.
  // eslint-disable-next-line @typescript-eslint/no-implied-eval, @typescript-eslint/no-unsafe-call -- the test runs the text of index.html's inline script, as the browser does; test code only.
  new Function(script)();
}

const html = document.documentElement;

describe('the inline theme script of index.html', () => {
  it('runs in the head, before the app is loaded', () => {
    expect(inline).not.toBeNull();
    const head = indexHtml.slice(0, indexHtml.indexOf('</head>'));
    expect(head).toContain(inline?.[0]);
    expect(indexHtml.indexOf(inline?.[0] ?? '')).toBeLessThan(indexHtml.indexOf('<script type="module"'));
  });

  it('uses the key and the query the provider uses', () => {
    expect(script).toContain(`'${themeStorageKey}'`);
    expect(script).toContain(`'${darkSchemeQuery}'`);
  });

  it.each([
    [null, false, 'light', 'system'],
    [null, true, 'dark', 'system'],
    ['system', true, 'dark', 'system'],
    ['light', true, 'light', 'light'],
    ['dark', false, 'dark', 'dark'],
    ['purple', true, 'dark', 'system'],
  ])(
    'with %j stored and a system that prefers dark: %j, shows %s (setting %s)',
    (stored, prefersDark, theme, setting) => {
      if (stored !== null) {
        window.localStorage.setItem(themeStorageKey, stored);
      }

      stubColourScheme(prefersDark);

      runScript();

      expect(html).toHaveAttribute('data-theme', theme);
      expect(html).toHaveAttribute('data-theme-setting', setting);
    },
  );

  it('falls back to the system when storage cannot be read', () => {
    stubColourScheme(true);
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('Storage is disabled.', 'SecurityError');
    });

    runScript();

    expect(html).toHaveAttribute('data-theme', 'dark');
  });

  it('has the SHA-256 the content security policy lists', () => {
    const sha256 = createHash('sha256').update(script, 'utf8').digest('base64');

    expect(sha256).toBe(pinnedSha256);
  });
});
