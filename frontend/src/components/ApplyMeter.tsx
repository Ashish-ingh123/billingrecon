import { Icon } from '../brand/Icon';
import { formatDecimal } from '../format';
import type { Paise } from '../bills/lineMath';
import { asRupees } from '../money';
import styles from './ApplyMeter.module.css';

/**
 * How much of what is available a form applies (ADR 0049, Prompt 9.3b): a bar of the applied amount against the cap, the
 * three figures in words, and, before anything is sent, a warning in words when the amounts pass the cap. The figures are
 * the paise the form already holds; nothing is computed here but their display.
 */
export function ApplyMeter({
  label,
  appliedLabel,
  cap,
  applied,
}: {
  /** What is available: "Unapplied cash", "Available credit". */
  label: string;
  /** What the form applies: "Allocated here". */
  appliedLabel: string;
  cap: Paise;
  applied: Paise;
}) {
  const left = cap - applied;
  const over = left < 0n;

  return (
    <div className={`${styles.meter} ${over ? styles.over : ''}`}>
      <meter
        className={styles.bar}
        min={0}
        max={Number(cap > 0n ? cap : 1n)}
        value={Number(applied > cap ? cap : applied)}
        aria-label={`${appliedLabel} of the ${label.toLowerCase()}`}
      />
      <p className={styles.figures} aria-live="polite">
        {label}: ₹{formatDecimal(asRupees(cap))} · {appliedLabel}: ₹{formatDecimal(asRupees(applied))} · Left:{' '}
        <strong>₹{formatDecimal(asRupees(left))}</strong>
      </p>
      {over && (
        <p className={styles.warning} role="alert">
          <Icon name="alert" />
          More than the {label.toLowerCase()} by ₹{formatDecimal(asRupees(-left))}: lower an amount before sending.
        </p>
      )}
    </div>
  );
}
