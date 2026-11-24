import type { ReactNode } from 'react';
import type { IconName } from '../brand/Icon';
import type { Tone } from './tones';
import { Icon } from '../brand/Icon';
import styles from './Badge.module.css';
import { toneIcons } from './tones';

export type { Tone } from './tones';

/**
 * A status shown as a small outlined label in the colour of its meaning, with an icon: the status's own (ADR 0049) or,
 * without one, the icon of its meaning (ADR 0048).
 */
export function Badge({ tone, icon, children }: { tone: Tone; icon?: IconName; children: ReactNode }) {
  return (
    <span className={`${styles.badge} ${styles[tone]}`}>
      <Icon name={icon ?? toneIcons[tone]} className={styles.icon} />
      {children}
    </span>
  );
}
