import type { UseQueryResult } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { Link } from 'react-router';
import { problemOf } from '../api/call';
import { documentLink, requestLabels } from '../approvals/api';
import { useSession } from '../auth/AuthContext';
import { useCan } from '../auth/permissions';
import table from '../components/DataTable.module.css';
import { ProblemAlert } from '../components/ProblemAlert';
import { formatDate, formatDecimal } from '../format';
import { methodLabels } from '../payments/api';
import { signedPaise } from '../reports/csv';
import { counted } from '../reports/view';
import type { DashboardPeriod } from './api';
import { useDriftPanel, usePendingPanel, useRecentPayments, useTopPayers } from './api';
import styles from './Dashboard.module.css';

/**
 * A panel of the dashboard (ADR 0044 point 4): its title and "View all", then its own loading skeleton, its own error (the
 * API's ProblemDetails), its empty state or its content, so that one failed request never blanks the page.
 */
export function Panel<T>({
  title,
  viewAll,
  badge,
  query,
  isEmpty,
  empty,
  className,
  children,
}: {
  title: string;
  viewAll?: { to: string; label?: string };
  badge?: (data: T) => ReactNode;
  query: UseQueryResult<T>;
  isEmpty: (data: T) => boolean;
  empty: string;
  className?: string;
  children: (data: T) => ReactNode;
}) {
  const headingId = `panel-${title.toLowerCase().replace(/[^a-z]+/g, '-')}`;
  return (
    <section className={`${styles.panel} ${className ?? ''}`} aria-labelledby={headingId}>
      <div className={styles.panelHead}>
        <h2 id={headingId}>
          {title} {query.data !== undefined && badge?.(query.data)}
        </h2>
        {viewAll !== undefined && <Link to={viewAll.to}>{viewAll.label ?? 'View all'} →</Link>}
      </div>
      {query.data !== undefined ? (
        isEmpty(query.data) ? (
          <p className={styles.empty}>{empty}</p>
        ) : (
          children(query.data)
        )
      ) : query.error !== null ? (
        <ProblemAlert problem={problemOf(query.error)} />
      ) : (
        <div className={styles.skeleton} role="status" aria-label={`Loading ${title.toLowerCase()}`} />
      )}
    </section>
  );
}

const periodSearch = ({ from, to }: DashboardPeriod) => (from === '' ? '' : `&from=${from}&to=${to}`);

export function RecentPaymentsPanel({ period }: { period: DashboardPeriod }) {
  const query = useRecentPayments(period);
  return (
    <Panel
      title="Recent payments"
      viewAll={{ to: `/reports/payments?order=NewestFirst${periodSearch(period)}` }}
      query={query}
      isEmpty={(data) => data.items.length === 0}
      empty="No payment in this period."
    >
      {(data) => (
        <div className={table.wrap}>
          <table className={table.table} aria-label="Recent payments">
            <thead>
              <tr>
                <th scope="col">Date</th>
                <th scope="col">Payment</th>
                <th scope="col">Payer</th>
                <th scope="col">Method</th>
                <th scope="col" className={table.number}>
                  Amount (₹)
                </th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((payment) => (
                <tr key={payment.paymentId}>
                  <td>{formatDate(payment.paymentDate)}</td>
                  <td>
                    <Link to={`/payments/${payment.paymentId}`}>{payment.number}</Link>
                  </td>
                  <td>{payment.payerName}</td>
                  <td>{methodLabels[payment.method]}</td>
                  <td className={table.number}>{formatDecimal(payment.amount)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Panel>
  );
}

export function TopPayersPanel() {
  const query = useTopPayers();
  return (
    <Panel
      title="Top outstanding payers"
      viewAll={{ to: '/reports/outstanding' }}
      query={query}
      isEmpty={(data) => data.items.length === 0}
      empty="No payer owes anything."
    >
      {(data) => {
        const largest = data.items.reduce((most, payer) => {
          const net = signedPaise(payer.netOutstanding);
          return net > most ? net : most;
        }, 0n);
        return (
          <>
            <div className={table.wrap}>
              <table className={table.table} aria-label="Top outstanding payers">
                <thead>
                  <tr>
                    <th scope="col">Payer</th>
                    <th scope="col">
                      <span className={styles.visuallyHidden}>Share of the largest</span>
                    </th>
                    <th scope="col" className={table.number}>
                      Net outstanding (₹)
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((payer) => {
                    const net = signedPaise(payer.netOutstanding);
                    const width = largest > 0n && net > 0n ? Number((net * 1000n) / largest) / 10 : 0;
                    return (
                      <tr key={payer.payerId}>
                        <td>
                          <Link to={`/payers/${payer.payerId}/statement`}>{payer.payerName}</Link>
                        </td>
                        <td aria-hidden="true">
                          <div className={styles.bar}>
                            <div className={styles.barFill} style={{ width: `${String(width)}%` }} />
                          </div>
                        </td>
                        <td className={table.number}>{formatDecimal(payer.netOutstanding)}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
            <p className={styles.small}>
              {counted(data.totals?.payers ?? data.totalCount, 'payer owes', 'payers owe')} or hold something today.
            </p>
          </>
        );
      }}
    </Panel>
  );
}

/** The oldest requests that wait; a checker reviews one in the inbox, never their own request (ADR 0042 point 3). */
export function PendingApprovalsPanel() {
  const query = usePendingPanel();
  const canDecide = useCan('Decide');
  const session = useSession();
  return (
    <Panel
      title="Pending approvals"
      viewAll={{ to: canDecide ? '/approvals' : '/reports/pending-approvals' }}
      badge={(data) => <span className={styles.small}>{String(data.totalCount)} waiting</span>}
      query={query}
      isEmpty={(data) => data.items.length === 0}
      empty="Nothing waits for a decision."
    >
      {(data) => (
        <>
          <div className={table.wrap}>
            <table className={table.table} aria-label="Pending approvals">
              <thead>
                <tr>
                  <th scope="col">Request</th>
                  <th scope="col" className={table.number}>
                    Amount (₹)
                  </th>
                  <th scope="col">Waiting</th>
                  <th scope="col">
                    <span className={styles.visuallyHidden}>Review</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((request) => {
                  const own = request.makerUserId === session.userId;
                  const link = documentLink(request);
                  return (
                    <tr key={request.approvalRequestId}>
                      <td>
                        {requestLabels[request.documentType]}
                        <span className={styles.small}>
                          {link === null ? (
                            request.payerName
                          ) : (
                            <Link to={link.to}>{request.payerName ?? link.label}</Link>
                          )}
                        </span>
                      </td>
                      <td className={table.number}>{request.amount === null ? '—' : formatDecimal(request.amount)}</td>
                      <td>{request.waitingDays === 0 ? 'today' : counted(request.waitingDays, 'day', 'days')}</td>
                      <td>
                        {own ? (
                          <span className={styles.small}>Your request</span>
                        ) : (
                          canDecide && <Link to={`/approvals?documentType=${request.documentType}`}>Review</Link>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
          <p className={styles.small}>
            Oldest first. A request you made yourself has no Review: the checker must be someone else.
          </p>
        </>
      )}
    </Panel>
  );
}

export function DriftPanel() {
  const query = useDriftPanel();
  return (
    <Panel
      title="Reconciliation exceptions"
      viewAll={{ to: '/reports/reconciliation' }}
      badge={(data) => <span className={styles.small}>{String(data.totalCount)} rows</span>}
      query={query}
      isEmpty={(data) => data.items.length === 0}
      empty="No drift: every cached balance equals the ledger and every number series is gapless."
    >
      {(data) => (
        <>
          <div className={table.wrap}>
            <table className={table.table} aria-label="Reconciliation exceptions">
              <thead>
                <tr>
                  <th scope="col">Check</th>
                  <th scope="col">Reference</th>
                  <th scope="col" className={table.number}>
                    Cached
                  </th>
                  <th scope="col" className={table.number}>
                    Ledger
                  </th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((row) => (
                  <tr key={`${row.identity}-${row.subjectId}`}>
                    <td>{row.identity}</td>
                    <td>{row.reference}</td>
                    <td className={table.number}>{formatDecimal(row.cached)}</td>
                    <td className={table.number}>{formatDecimal(row.ledger)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <p className={styles.small}>Evaluated now from the reconciliation; the report keeps no history.</p>
        </>
      )}
    </Panel>
  );
}
