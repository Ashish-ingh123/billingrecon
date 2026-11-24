import { useState, type ReactNode } from 'react';
import { Icon } from '../brand/Icon';
import { Money } from './Money';
import styles from './TotalsCard.module.css';

export interface TotalsRow {
  label: string;
  value: number | string;
  /** A total: it stands out from its parts. */
  strong?: boolean;
  /** A rule above the row: what follows is not part of the sum above (the balance due after the total). */
  apart?: boolean;
  credit?: boolean;
}

/**
 * The totals of a document in a card (ADR 0049): the parts quiet, a total larger and bolder, the balance under a rule.
 * An estimate says so in its head until the server's figures replace it. A figure that changes is marked for a moment;
 * the figure itself never moves, and with reduced motion nothing is marked.
 */
export function TotalsCard({
  rows,
  label,
  estimate,
  sticky = false,
}: {
  rows: readonly TotalsRow[];
  /** The name of the list of figures for assistive technology. */
  label?: string;
  /** The words under an estimate: what replaces it, and when. */
  estimate?: ReactNode;
  /** The card stays in view while what it sums scrolls (the editor's lines). */
  sticky?: boolean;
}) {
  return (
    <div className={`${styles.card} ${sticky ? styles.sticky : ''}`}>
      {estimate !== undefined && (
        <p className={styles.head}>
          <span className={styles.tag}>
            <Icon name="pencil" />
            Estimate
          </span>
        </p>
      )}
      <dl className={styles.list} aria-label={label}>
        {rows.map((row) => (
          <Figure key={row.label} row={row} />
        ))}
      </dl>
      {estimate !== undefined && <p className={styles.note}>{estimate}</p>}
    </div>
  );
}

function Figure({ row }: { row: TotalsRow }) {
  const value = String(row.value);
  // How many times the figure has changed since it was first shown: the mark plays from the first change on.
  const [seen, setSeen] = useState({ value, changes: 0 });
  if (seen.value !== value) {
    setSeen({ value, changes: seen.changes + 1 });
  }

  return (
    <div
      className={`${styles.row} ${row.strong === true ? styles.strong : ''} ${row.apart === true ? styles.apart : ''}`}
    >
      <dt>{row.label}</dt>
      <dd key={seen.changes} className={seen.changes > 0 ? styles.changed : undefined}>
        <Money
          value={row.value}
          strong={row.strong === true}
          {...(row.credit === undefined ? {} : { credit: row.credit })}
        />
      </dd>
    </div>
  );
}
