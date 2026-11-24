import type { ReactNode } from 'react';
import { Icon, type IconName } from '../brand/Icon';
import styles from './Banner.module.css';
import type { Tone } from './tones';

const icons: Record<Exclude<Tone, 'muted'>, IconName> = {
  accent: 'help',
  success: 'check-circle',
  warning: 'clock',
  danger: 'ban',
};

/**
 * A strip across a document that says what state it is in and what follows (ADR 0049): a bill waiting for approval, a
 * voided draft. A word and an icon carry the meaning; the soft ground of the tone only frames it. A banner that is news
 * (`live`) is announced once.
 */
export function Banner({
  tone,
  title,
  children,
  live = false,
}: {
  tone: Exclude<Tone, 'muted'>;
  title: string;
  children?: ReactNode;
  live?: boolean;
}) {
  return (
    <div className={`${styles.banner} ${styles[tone]}`} role={live ? 'status' : undefined}>
      <Icon name={icons[tone]} className={styles.icon} />
      <div className={styles.body}>
        <p className={styles.title}>{title}</p>
        {children !== undefined && <p className={styles.text}>{children}</p>}
      </div>
    </div>
  );
}
