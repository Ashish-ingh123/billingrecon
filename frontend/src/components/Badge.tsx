import type { ReactNode } from 'react';
import styles from './Badge.module.css';

/** What a status means at a glance, as a colour of the tokens. */
export type Tone = 'muted' | 'accent' | 'success' | 'warning' | 'danger';

/** A status shown as a small outlined label in the colour of its meaning. */
export function Badge({ tone, children }: { tone: Tone; children: ReactNode }) {
  return <span className={`${styles.badge} ${styles[tone]}`}>{children}</span>;
}
