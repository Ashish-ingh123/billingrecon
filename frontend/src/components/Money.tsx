import { formatMoney } from '../format';
import styles from './Money.module.css';

/**
 * An amount of money (ADR 0049): the rupee sign, Indian grouping and tabular figures, its text exactly that of
 * `formatMoney`. A credit, or any negative amount, says so by a sign and a word, never by a colour alone: a credit of 500
 * reads "−₹500.00 credit".
 */
export function Money({
  value,
  credit = false,
  strong = false,
}: {
  value: number | string;
  /** The amount is a credit to the payer: shown with a minus sign and the word "credit". */
  credit?: boolean;
  /** A total: set larger and bolder than its parts. */
  strong?: boolean;
}) {
  const amount = Number(value);
  const shown = credit && amount > 0 ? -amount : amount;
  return (
    <span className={`${styles.money} ${strong ? styles.strong : ''}`}>
      {formatMoney(shown)}
      {credit && <span className={styles.word}> credit</span>}
      {!credit && shown < 0 && <span className={styles.word}> negative</span>}
    </span>
  );
}
