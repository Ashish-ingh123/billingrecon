/**
 * The theme (ADR 0038, point 7): the user's setting (System, Light or Dark), kept in localStorage, and the theme it
 * resolves to, set as data-theme on <html>. The inline script of index.html does the same before React mounts, with the
 * same key and the same attributes; a test holds the two together.
 */

export const themeStorageKey = 'billingrecon.theme';

export const darkSchemeQuery = '(prefers-color-scheme: dark)';

export const themeSettings = ['system', 'light', 'dark'] as const;

export type ThemeSetting = (typeof themeSettings)[number];

export type Theme = 'light' | 'dark';

/** The stored setting, or System when there is none or storage cannot be read. */
export function readThemeSetting(): ThemeSetting {
  try {
    const stored = window.localStorage.getItem(themeStorageKey);
    return stored === 'light' || stored === 'dark' ? stored : 'system';
  } catch {
    return 'system';
  }
}

/** Keeps the setting for the next visit; a browser that refuses storage keeps it for this page only. */
export function storeThemeSetting(setting: ThemeSetting): void {
  try {
    window.localStorage.setItem(themeStorageKey, setting);
  } catch {
    // Storage is off: the choice still applies until the page is left.
  }
}

/** The theme a setting means, given whether the system prefers dark. */
export function resolveTheme(setting: ThemeSetting, systemPrefersDark: boolean): Theme {
  return setting === 'system' ? (systemPrefersDark ? 'dark' : 'light') : setting;
}

/** Sets the theme on <html>, where the tokens read it. */
export function applyTheme(setting: ThemeSetting, theme: Theme): void {
  document.documentElement.setAttribute('data-theme', theme);
  document.documentElement.setAttribute('data-theme-setting', setting);
}
