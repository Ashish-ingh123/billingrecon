import { useCallback, useLayoutEffect, useMemo, useState, useSyncExternalStore, type ReactNode } from 'react';
import {
  applyTheme,
  darkSchemeQuery,
  readThemeSetting,
  resolveTheme,
  storeThemeSetting,
  type ThemeSetting,
} from './theme';
import { ThemeContext } from './ThemeContext';

function subscribeToSystemScheme(onChange: () => void): () => void {
  const query = window.matchMedia(darkSchemeQuery);
  query.addEventListener('change', onChange);
  return () => query.removeEventListener('change', onChange);
}

function systemPrefersDark(): boolean {
  return window.matchMedia(darkSchemeQuery).matches;
}

/**
 * Holds the theme setting and applies it to <html> (ADR 0038, point 7). With System, it follows the operating system's
 * scheme and reacts when that changes; a choice is kept for the next visit and shown at once, without a reload.
 */
export function ThemeProvider({ children }: { children: ReactNode }) {
  const [setting, setSetting] = useState<ThemeSetting>(readThemeSetting);
  const prefersDark = useSyncExternalStore(subscribeToSystemScheme, systemPrefersDark);
  const theme = resolveTheme(setting, prefersDark);

  // Before the browser paints, so a change never shows a frame of the old theme.
  useLayoutEffect(() => applyTheme(setting, theme), [setting, theme]);

  const choose = useCallback((next: ThemeSetting) => {
    storeThemeSetting(next);
    setSetting(next);
  }, []);

  const state = useMemo(() => ({ setting, theme, choose }), [setting, theme, choose]);

  return <ThemeContext value={state}>{children}</ThemeContext>;
}
