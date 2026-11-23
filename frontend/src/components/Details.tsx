import type { ReactNode } from 'react';
import styles from './Details.module.css';

/** The fields of a record, as terms and values; a value that is empty is shown as a dash. */
export function Details({ items }: { items: readonly (readonly [string, ReactNode])[] }) {
  return (
    <dl className={styles.details}>
      {items.map(([term, value]) => (
        <div key={term} className={styles.item}>
          <dt>{term}</dt>
          <dd>{value === null || value === undefined || value === '' ? '—' : value}</dd>
        </div>
      ))}
    </dl>
  );
}
