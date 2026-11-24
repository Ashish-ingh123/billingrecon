import { Link } from 'react-router';
import { Icon, type IconName } from '../brand/Icon';
import { PageHeader } from '../components/PageHeader';
import styles from './Reports.module.css';

/** The six reports of R1 (BR-901) and their screens, as the index shows them (ADR 0043 point 2). */
const reportGroups: readonly {
  title: string;
  reports: readonly { to: string; name: string; about: string; icon: IconName }[];
}[] = [
  {
    title: 'Bills',
    reports: [
      {
        to: '/reports/bills',
        name: 'Bill register',
        about: 'The posted bills of a period, with what they still owe.',
        icon: 'receipt',
      },
    ],
  },
  {
    title: 'Payments',
    reports: [
      {
        to: '/reports/payments',
        name: 'Payment register',
        about: 'The payments of a period, reversed ones included.',
        icon: 'wallet',
      },
      { to: '/reports/collection', name: 'Daily collection', about: 'The money of one day by method.', icon: 'card' },
    ],
  },
  {
    title: 'Balances',
    reports: [
      {
        to: '/reports/outstanding',
        name: 'Outstanding by payer',
        about: 'What each payer owes, less the cash and credit they hold.',
        icon: 'bar-chart',
      },
    ],
  },
  {
    title: 'Cash and credit held',
    reports: [
      {
        to: '/reports/unapplied-cash',
        name: 'Unapplied cash',
        about: 'The payments that still hold money.',
        icon: 'wallet',
      },
      {
        to: '/reports/available-credit',
        name: 'Available credit',
        about: 'The credit notes that still hold credit.',
        icon: 'receipt-minus',
      },
    ],
  },
  {
    title: 'Approvals',
    reports: [
      {
        to: '/reports/pending-approvals',
        name: 'Pending approvals',
        about: 'What waits for a second person.',
        icon: 'clock',
      },
    ],
  },
  {
    title: 'Control',
    reports: [
      {
        to: '/reports/reconciliation',
        name: 'Reconciliation',
        about: 'Every cached balance against the ledger, and every number series checked for gaps.',
        icon: 'shield-check',
      },
    ],
  },
];

/** The reports (BR-901): every role reads them; each shows the server's figures and exports every row as CSV. */
export function ReportsIndex() {
  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Reports"
        subtitle="Every figure is the server's and ties to the ledger. Each report exports every row of its filter as CSV."
      />
      <ul className={styles.groups}>
        {reportGroups.map((group) => (
          <li key={group.title} className={styles.group}>
            <h2>{group.title}</h2>
            <ul className={styles.cards}>
              {group.reports.map((report) => (
                // A card that opens its report: only the name is the link, and the card takes its click (ADR 0050).
                <li key={report.to} className={styles.card}>
                  <span className={styles.cardIcon} aria-hidden="true">
                    <Icon name={report.icon} />
                  </span>
                  <span>
                    <Link className={styles.cardLink} to={report.to}>
                      {report.name}
                    </Link>
                    <p>{report.about}</p>
                  </span>
                </li>
              ))}
            </ul>
          </li>
        ))}
      </ul>
    </section>
  );
}
