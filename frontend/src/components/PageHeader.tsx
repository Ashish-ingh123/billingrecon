import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { Icon } from '../brand/Icon';
import styles from './PageHeader.module.css';

/** The top of a screen: where it sits, its title, what it is, and its actions. */
export function PageHeader({
  title,
  back,
  subtitle,
  actions,
}: {
  title: ReactNode;
  back?: { to: string; label: string };
  subtitle?: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <header className={styles.header}>
      {back !== undefined && (
        <Link className={styles.back} to={back.to}>
          <Icon name="arrow-left" />
          {`Back to ${back.label}`}
        </Link>
      )}
      <div className={styles.row}>
        <div>
          <h1 id="page-title" className={styles.title}>
            {title}
          </h1>
          {subtitle !== undefined && <p className={styles.subtitle}>{subtitle}</p>}
        </div>
        {actions !== undefined && <div className={styles.actions}>{actions}</div>}
      </div>
    </header>
  );
}
