import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useApiClient } from '../api/ApiClientContext';
import { answer } from '../api/call';
import { useSession } from '../auth/AuthContext';
import { Icon, type IconName } from '../brand/Icon';
import { formatInstant } from '../format';
import styles from './AuditTrail.module.css';
import table from './DataTable.module.css';
import { Pagination } from './ListControls';
import { pageSize } from './listParams';
import { LoadState } from './Loading';

/** How an audit action reads: `LinesReplaced` as "Lines replaced". */
function actionLabel(action: string): string {
  const words = action.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

/** The icon of an audit action on the timeline (ADR 0049): what it did, at a glance; the word is always beside it. */
function actionIcon(action: string): IconName {
  if (/Voided|Rejected|Disabled|Deactivated/.test(action)) return 'ban';
  if (/Revers/.test(action) && !/Requested/.test(action)) return 'undo';
  if (/Requested|Submitted|Pending/.test(action)) return 'clock';
  if (/Approved|Settled|Enabled|Activated|Allocated|Applied/.test(action)) return 'check-circle';
  if (/Posted|Recorded/.test(action)) return 'stamp';
  if (/Drafted|Created|Updated|Replaced|Changed/.test(action)) return 'pencil';
  return 'dot';
}

/**
 * The audit trail of a record (US-43, BR-701 to BR-703, ADR 0040 point 7): every state change, oldest first, with who
 * made it, when, why, the document's number, the correlation id of the request, and the stored record before and after.
 * A user is named "you" when it is the signed-in user, and by id otherwise: only a TenantAdmin may read users.
 */
export function AuditTrail({ entityType, entityId }: { entityType: string; entityId: string }) {
  const client = useApiClient();
  const session = useSession();
  const [page, setPage] = useState(1);
  const trail = useQuery({
    queryKey: ['audit', entityType, entityId, page],
    queryFn: () =>
      answer(
        client.GET('/api/v1/audit-records', {
          params: { query: { EntityType: entityType, EntityId: entityId, Page: page, PageSize: pageSize } },
        }),
      ),
  });

  if (trail.data === undefined) {
    return <LoadState error={trail.error} what="the audit trail" />;
  }

  if (trail.data.items.length === 0) {
    return <p className={table.empty}>Nothing is recorded yet.</p>;
  }

  return (
    <>
      <ol className={styles.timeline} aria-label="Changes, oldest first">
        {trail.data.items.map((record) => (
          <li key={String(record.id)} className={styles.entry}>
            <span className={styles.marker}>
              <Icon name={actionIcon(record.action)} />
            </span>
            <div className={styles.body}>
              <p className={styles.headline}>
                <span className={styles.action}>{actionLabel(record.action)}</span>
                {record.documentNumber !== null && record.documentNumber !== undefined && (
                  <span className={styles.number}>{record.documentNumber}</span>
                )}
              </p>
              <p className={styles.meta}>
                <time dateTime={record.occurredAtUtc}>{formatInstant(record.occurredAtUtc)}</time> · by{' '}
                {record.userId === null ? (
                  <span>The system</span>
                ) : record.userId === session.userId ? (
                  <span>You</span>
                ) : (
                  <code title={record.userId}>{record.userId.slice(0, 8)}</code>
                )}
              </p>
              {record.reason !== null && record.reason !== undefined && record.reason !== '' && (
                <p className={styles.reason}>{record.reason}</p>
              )}
              <details className={styles.record}>
                <summary>Before and after</summary>
                <p className={styles.reference}>
                  Reference: <code>{record.correlationId}</code>
                </p>
                {record.before !== null && record.before !== undefined && (
                  <>
                    <p className={styles.caption}>Before</p>
                    <pre className={styles.json}>{JSON.stringify(record.before, null, 2)}</pre>
                  </>
                )}
                <p className={styles.caption}>After</p>
                <pre className={styles.json}>{JSON.stringify(record.after, null, 2)}</pre>
              </details>
            </div>
          </li>
        ))}
      </ol>
      <Pagination page={page} totalCount={Number(trail.data.totalCount)} onChange={setPage} />
    </>
  );
}
