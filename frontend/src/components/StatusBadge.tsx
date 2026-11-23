import styles from './StatusBadge.module.css';

/** The status of a patient, a payer or a service item. */
export function StatusBadge({ status }: { status: 'Active' | 'Inactive' }) {
  return <span className={status === 'Active' ? styles.active : styles.inactive}>{status}</span>;
}
