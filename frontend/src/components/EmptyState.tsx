import type { ReactNode } from 'react';
import { Icon, type IconName } from '../brand/Icon';
import styles from './EmptyState.module.css';

/**
 * A list with nothing to show (ADR 0048): a small drawing of our own, what is (not) there, and, for a role that may,
 * the next thing to do. The message keeps the words it had, which the tests read.
 */
export function EmptyState({ icon, children, action }: { icon: IconName; children: ReactNode; action?: ReactNode }) {
  return (
    <div className={styles.empty}>
      <span className={styles.drawing} aria-hidden="true">
        <Icon name={icon} />
      </span>
      <p className={styles.message}>{children}</p>
      {action}
    </div>
  );
}
