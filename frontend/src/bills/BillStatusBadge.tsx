import { billStatusLabels, type BillStatus } from './api';
import styles from './BillStatusBadge.module.css';

/** The status of a bill, each in its own colour of the tokens. */
export function BillStatusBadge({ status }: { status: BillStatus }) {
  return <span className={`${styles.badge} ${styles[status]}`}>{billStatusLabels[status]}</span>;
}
