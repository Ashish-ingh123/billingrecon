import type { ReactNode } from 'react';
import { Link } from 'react-router';
import buttons from '../components/buttons.module.css';
import styles from './Printing.module.css';

/** The way from a document's page to its printed page (Prompt 10.4b, ADR 0054): shown only for what can be printed. */
export function PrintLink({ to, children }: { to: string; children: ReactNode }) {
  return (
    <p className={styles.printLink}>
      <Link className={buttons.secondary} to={to}>
        {children}
      </Link>
    </p>
  );
}
