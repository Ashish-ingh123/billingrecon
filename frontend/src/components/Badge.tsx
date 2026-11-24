import type { ReactNode } from 'react';
import type { Tone } from './tones';
import { Icon } from '../brand/Icon';
import styles from './Badge.module.css';
import { toneIcons } from './tones';

export type { Tone } from './tones';

/** A status shown as a small outlined label in the colour of its meaning, with the icon of that meaning. */
export function Badge({ tone, children }: { tone: Tone; children: ReactNode }) {
  return (
    <span className={`${styles.badge} ${styles[tone]}`}>
      <Icon name={toneIcons[tone]} className={styles.icon} />
      {children}
    </span>
  );
}
