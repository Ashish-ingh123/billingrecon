import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useApiClient } from '../api/ApiClientContext';
import { answer } from '../api/call';
import { useSession } from '../auth/AuthContext';
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
      <div className={table.wrap}>
        <table className={table.table}>
          <thead>
            <tr>
              <th scope="col">When</th>
              <th scope="col">What</th>
              <th scope="col">By</th>
              <th scope="col">Number</th>
              <th scope="col">Reason</th>
              <th scope="col">Record</th>
            </tr>
          </thead>
          <tbody>
            {trail.data.items.map((record) => (
              <tr key={String(record.id)}>
                <td>{formatInstant(record.occurredAtUtc)}</td>
                <td>{actionLabel(record.action)}</td>
                <td>
                  {record.userId === null ? (
                    'The system'
                  ) : record.userId === session.userId ? (
                    'You'
                  ) : (
                    <code title={record.userId}>{record.userId.slice(0, 8)}</code>
                  )}
                </td>
                <td>{record.documentNumber}</td>
                <td>{record.reason}</td>
                <td>
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
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <Pagination page={page} totalCount={Number(trail.data.totalCount)} onChange={setPage} />
    </>
  );
}
