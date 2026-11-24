import { useId } from 'react';
import { Icon, type IconName } from '../brand/Icon';
import type { ThemeSetting } from './theme';
import { useTheme } from './ThemeContext';
import styles from './ThemeToggle.module.css';

const options: Record<ThemeSetting, { label: string; icon: IconName }> = {
  light: { label: 'Light', icon: 'sun' },
  dark: { label: 'Dark', icon: 'moon' },
  system: { label: 'System', icon: 'monitor' },
};

// Light, Dark, then System: the order the pill shows them in.
const order: readonly ThemeSetting[] = ['light', 'dark', 'system'];

/**
 * The theme choice (ADR 0038 point 7, ADR 0048): a pill of three, Light, Dark and System, with a highlight that slides
 * to the one chosen. Underneath it is a group of three radio buttons named "Theme", so the arrow keys move the choice,
 * a screen reader announces "Theme, radio group", and the choice applies at once. Each shows only its icon; its name is
 * its word.
 */
export function ThemeToggle() {
  const { setting, choose } = useTheme();
  const name = useId();

  return (
    <div role="radiogroup" aria-label="Theme" className={styles.toggle} data-chosen={setting}>
      <span className={styles.thumb} aria-hidden="true" />
      {order.map((value) => {
        const { label, icon } = options[value];
        return (
          <label key={value} className={styles.option} title={label}>
            <input
              type="radio"
              name={name}
              value={value}
              className={styles.input}
              checked={setting === value}
              onChange={() => choose(value)}
              aria-label={label}
            />
            <Icon name={icon} className={styles.icon} />
          </label>
        );
      })}
    </div>
  );
}
