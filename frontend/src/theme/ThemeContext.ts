import { createContext, useContext } from 'react';
import type { Theme, ThemeSetting } from './theme';

export interface ThemeState {
  /** What the user chose. */
  setting: ThemeSetting;
  /** What is shown: the choice, or the system's when the choice is System. */
  theme: Theme;
  choose: (setting: ThemeSetting) => void;
}

export const ThemeContext = createContext<ThemeState | null>(null);

export function useTheme(): ThemeState {
  const state = useContext(ThemeContext);
  if (state === null) {
    throw new Error('useTheme is used outside ThemeProvider.');
  }

  return state;
}
