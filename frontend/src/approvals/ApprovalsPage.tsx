import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useId, useRef, useState, type ReactNode } from 'react';
import { Link, useSearchParams } from 'react-router';
import { useApiClient } from '../api/ApiClientContext';
import { useSession } from '../auth/AuthContext';
import buttons from '../components/buttons.module.css';
import type { ComparedField } from '../components/conflict';
import table from '../components/DataTable.module.css';
import { Details } from '../components/Details';
import forms from '../components/forms.module.css';
import { Pagination } from '../components/ListControls';
import listStyles from '../components/ListControls.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { ReasonForm } from '../components/ReasonForm';
import { useDocumentAction } from '../components/useDocumentAction';
import { formatDecimal, formatInstant } from '../format';
import {
  approvalDocumentTypes,
  approvalKeys,
  decisionsChanged,
  documentLink,
  documentTypeLabels,
  readDecidable,
  requestLabels,
  usePendingApprovals,
  type ApprovalDocumentType,
  type ApprovalListParams,
  type Decidable,
  type PendingApproval,
} from './api';

/**
 * The checker's inbox (BR-603 to BR-606, ADR 0042 point 3): every request that waits for a second person, the oldest
 * first, with its payer, amount, maker and the maker's reason. A checker reviews one in place, reading its document as it
 * is now, and approves (a comment is optional) or rejects (a comment is required). A request a user made themself shows
 * no review: the maker never decides (BR-603), and the API refuses it too.
 */
export function ApprovalsPage() {
  const [query, setQuery] = useSearchParams();
  const session = useSession();
  const typeId = useId();
  const type = query.get('documentType') as ApprovalDocumentType | null;
  const page = Number.parseInt(query.get('page') ?? '1', 10);
  const params: ApprovalListParams = {
    documentType: type !== null && approvalDocumentTypes.includes(type) ? type : undefined,
    page: Number.isFinite(page) && page >= 1 ? page : 1,
  };
  const pending = usePendingApprovals(params);
  const [reviewing, setReviewing] = useState<string | null>(null);
  const [decided, setDecided] = useState<string | null>(null);

  const change = (next: Partial<ApprovalListParams>) => {
    const merged = { ...params, ...next, page: next.page ?? 1 };
    const search = new URLSearchParams();
    if (merged.documentType !== undefined) {
      search.set('documentType', merged.documentType);
    }

    if (merged.page > 1) {
      search.set('page', String(merged.page));
    }

    setReviewing(null);
    setQuery(search);
  };

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title="Approvals"
        subtitle="What waits for a second person: approving posts the document or makes the reversal; rejecting returns it to its maker."
      />
      {decided !== null && (
        <p className={forms.note} role="status">
          {decided}
        </p>
      )}
      <div className={listStyles.filters}>
        <div className={listStyles.field}>
          <label htmlFor={typeId}>Waiting</label>
          <select
            id={typeId}
            className={listStyles.input}
            value={params.documentType ?? ''}
            onChange={(event) =>
              change({ documentType: (event.target.value || undefined) as ApprovalDocumentType | undefined })
            }
          >
            <option value="">Everything</option>
            {approvalDocumentTypes.map((value) => (
              <option key={value} value={value}>
                {documentTypeLabels[value]}
              </option>
            ))}
          </select>
        </div>
      </div>
      {pending.data === undefined ? (
        <LoadState error={pending.error} what="the requests that wait" />
      ) : pending.data.items.length === 0 ? (
        <p className={table.empty}>Nothing waits for a decision.</p>
      ) : (
        <>
          <p className={forms.note}>
            {String(pending.data.totalCount)} waiting
            {pending.data.totals?.oldestRequestedAtUtc != null &&
              `, the oldest since ${formatInstant(pending.data.totals.oldestRequestedAtUtc)}`}
            .
          </p>
          <div className={table.wrap}>
            <table className={table.table}>
              <thead>
                <tr>
                  <th scope="col">Asked</th>
                  <th scope="col">Request</th>
                  <th scope="col">Payer</th>
                  <th scope="col" className={table.number}>
                    Amount (₹)
                  </th>
                  <th scope="col">By</th>
                  <th scope="col">Reason given</th>
                  <th scope="col">
                    <span className={forms.note}>Decision</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {pending.data.items.map((request) => {
                  const own = request.makerUserId === session.userId;
                  const link = documentLink(request);
                  return (
                    <Row
                      key={request.approvalRequestId}
                      request={request}
                      cells={
                        <>
                          <td>
                            {formatInstant(request.requestedAtUtc)}
                            <span className={forms.note}>
                              {' '}
                              ·{' '}
                              {request.waitingDays === 0
                                ? 'today'
                                : request.waitingDays === 1
                                  ? '1 day'
                                  : `${String(request.waitingDays)} days`}
                            </span>
                          </td>
                          <td>
                            {requestLabels[request.documentType]}
                            {link !== null && (
                              <>
                                {' '}
                                · <Link to={link.to}>{link.label}</Link>
                              </>
                            )}
                          </td>
                          <td>
                            {request.payerId === null ? (
                              '—'
                            ) : (
                              <Link to={`/payers/${request.payerId}`}>{request.payerName ?? 'The payer'}</Link>
                            )}
                          </td>
                          <td className={table.number}>
                            {request.amount === null ? '—' : formatDecimal(request.amount)}
                          </td>
                          <td>{own ? 'You' : request.makerUserId.slice(0, 8)}</td>
                          <td>{request.makerComment ?? '—'}</td>
                          <td>
                            {own ? (
                              <span className={forms.note}>Yours: waits for another checker</span>
                            ) : reviewing === request.approvalRequestId ? null : (
                              <button
                                type="button"
                                className={buttons.secondary}
                                onClick={() => {
                                  setDecided(null);
                                  setReviewing(request.approvalRequestId);
                                }}
                              >
                                Review
                              </button>
                            )}
                          </td>
                        </>
                      }
                      review={
                        !own && reviewing === request.approvalRequestId ? (
                          <ReviewPanel
                            request={request}
                            onClose={() => setReviewing(null)}
                            onDecided={(message) => {
                              setReviewing(null);
                              setDecided(message);
                            }}
                          />
                        ) : null
                      }
                    />
                  );
                })}
              </tbody>
            </table>
          </div>
          <Pagination
            page={params.page}
            totalCount={Number(pending.data.totalCount)}
            onChange={(next) => change({ page: next })}
          />
        </>
      )}
    </section>
  );
}

function Row({ request, cells, review }: { request: PendingApproval; cells: ReactNode; review: ReactNode }) {
  return (
    <>
      <tr>{cells}</tr>
      {review !== null && (
        <tr>
          <td colSpan={7} aria-label={`Review: ${requestLabels[request.documentType]}`}>
            {review}
          </td>
        </tr>
      )}
    </>
  );
}

/** The fields a conflict compares: the facts the checker was shown, by their labels. */
function comparedFacts(decidable: Decidable): ComparedField<Decidable>[] {
  return decidable.facts.map(([label]) => ({
    label,
    value: (record: Decidable) => record.facts.find(([name]) => name === label)?.[1] ?? '',
  }));
}

/**
 * One request under review: its document read as it is now, and the decision, sent with that document's version and the
 * key of its operation (ADR 0040 point 2). A conflict reads the document again and says what changed, for one: that
 * someone else decided first.
 */
function ReviewPanel({
  request,
  onClose,
  onDecided,
}: {
  request: PendingApproval;
  onClose: () => void;
  onDecided: (message: string) => void;
}) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const document = useQuery({
    queryKey: approvalKeys.review(request.approvalRequestId),
    queryFn: () => readDecidable(client, request),
    staleTime: 0,
  });
  // The decision being written: approve with an optional comment, or reject with a required one; null while neither is.
  const [deciding, setDeciding] = useState<'approve' | 'reject' | null>(null);

  if (document.data === undefined) {
    return <LoadState error={document.error} what="the document" />;
  }

  return (
    <Decision
      request={request}
      decidable={document.data}
      deciding={deciding}
      setDeciding={setDeciding}
      onClose={onClose}
      onReloaded={(latest) => queryClient.setQueryData(approvalKeys.review(request.approvalRequestId), latest)}
      onDone={(message) => {
        decisionsChanged(queryClient);
        onDecided(message);
      }}
    />
  );
}

function Decision({
  request,
  decidable,
  deciding,
  setDeciding,
  onClose,
  onReloaded,
  onDone,
}: {
  request: PendingApproval;
  decidable: Decidable;
  deciding: 'approve' | 'reject' | null;
  setDeciding: (next: 'approve' | 'reject' | null) => void;
  onClose: () => void;
  onReloaded: (latest: Decidable) => void;
  onDone: (message: string) => void;
}) {
  const client = useApiClient();
  const what = requestLabels[request.documentType].toLowerCase();
  // What a decision will say once done, set as it is sent.
  const messageRef = useRef('');
  const action = useDocumentAction<unknown, Decidable>({
    current: decidable,
    compared: comparedFacts(decidable),
    reload: () => readDecidable(client, request),
    onDone: () => onDone(messageRef.current),
    onReloaded: (latest) => {
      setDeciding(null);
      onReloaded(latest);
    },
  });

  const decide = (verb: 'approve' | 'reject', comment: string) => {
    messageRef.current =
      verb === 'approve'
        ? `Approved: ${what}${request.payerName === null ? '' : ` of ${request.payerName}`}.`
        : `Rejected: ${what}${request.payerName === null ? '' : ` of ${request.payerName}`}; it is back with its maker.`;
    action.run({
      identity: `POST ${decidable.path}/${verb} ${decidable.version} ${JSON.stringify(comment)}`,
      verb,
      send: async (key) => ({
        data: await (verb === 'approve' ? decidable.approve(comment, key) : decidable.reject(comment, key)),
        status: 200,
      }),
      done: () => messageRef.current,
    });
  };

  return (
    <div className={forms.section}>
      {action.notices}
      <Details items={decidable.facts.map(([label, value]) => [label, value])} />
      {deciding === 'approve' ? (
        <ReasonForm
          label="Comment (optional)"
          confirmLabel="Confirm: approve"
          tone="primary"
          optional
          busy={action.pending}
          onConfirm={(comment) => decide('approve', comment)}
          onCancel={() => setDeciding(null)}
        />
      ) : deciding === 'reject' ? (
        <ReasonForm
          label="Why is it rejected? The maker reads this."
          confirmLabel="Confirm: reject"
          busy={action.pending}
          onConfirm={(comment) => decide('reject', comment)}
          onCancel={() => setDeciding(null)}
        />
      ) : (
        <div className={forms.actions}>
          <button type="button" className={buttons.primary} onClick={() => setDeciding('approve')}>
            Approve
          </button>
          <button type="button" className={buttons.danger} onClick={() => setDeciding('reject')}>
            Reject
          </button>
          <button type="button" className={buttons.link} onClick={onClose}>
            Close
          </button>
        </div>
      )}
    </div>
  );
}
