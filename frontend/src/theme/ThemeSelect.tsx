import { useId } from 'react';
import { themeSettings, type ThemeSetting } from './theme';
import { useTheme } from './ThemeContext';
import styles from './ThemeSelect.module.css';

const labels: Record<ThemeSetting, string> = { system: 'System', light: 'Light', dark: 'Dark' };

/** The theme chooser of the top bar: System, Light or Dark, applied at once. */
export function ThemeSelect() {
  const { setting, choose } = useTheme();
  const id = useId();

  return (
    <div className={styles.field}>
      <label htmlFor={id} className={styles.label}>
        Theme
      </label>
      <select
        id={id}
        className={styles.select}
        value={setting}
        onChange={(event) => choose(event.target.value as ThemeSetting)}
      >
        {themeSettings.map((value) => (
          <option key={value} value={value}>
            {labels[value]}
          </option>
        ))}
      </select>
    </div>
  );
}
