import { useQuery, type QueryClient } from '@tanstack/react-query';
import { answer, ifMatch } from '../api/call';
import type { ApiClient } from '../api/client';
import { useApiClient } from '../api/ApiClientContext';
import type { components } from '../api/schema';
import { adjustmentStatusLabels, kindLabels } from '../adjustments/api';
import { billStatusLabels } from '../bills/api';
import { applicationStatusLabels, creditNoteStatusLabels } from '../creditNotes/api';
import { pageSize } from '../components/listParams';
import { formatDate, formatDecimal } from '../format';
import { allocationStatusLabels, methodLabels, paymentStatusLabels } from '../payments/api';
import { refundStatusLabels } from '../refunds/api';

/** A request that waits for a checker, as the pending approvals report answers it (`docs/api.md` section 8.15). */
export type PendingApproval = components['schemas']['PendingApprovalDto'];
export type ApprovalDocumentType = components['schemas']['ApprovalDocumentType'];

export const approvalDocumentTypes: readonly ApprovalDocumentType[] = [
  'Bill',
  'CreditNote',
  'Adjustment',
  'Refund',
  'Payment',
  'PaymentAllocation',
  'CreditNoteAllocation',
  'PrefixChange',
  'OpeningBatch',
  'CashierShift',
];

/** What a request asks, by the kind of its document: each kind has one action (ADR 0023 to ADR 0033). */
export const requestLabels: Record<ApprovalDocumentType, string> = {
  Bill: 'Post a bill',
  CreditNote: 'Post a credit note',
  Adjustment: 'Post an adjustment',
  Refund: 'Post a refund',
  Payment: 'Reverse a payment',
  PaymentAllocation: 'Reverse an allocation',
  CreditNoteAllocation: 'Reverse an application of credit',
  PrefixChange: 'Change a document prefix',
  OpeningBatch: 'Bring in opening balances',
  CashierShift: 'Close a shift with a difference',
};

/** The kinds as the filter names them. */
export const documentTypeLabels: Record<ApprovalDocumentType, string> = {
  Bill: 'Bills',
  CreditNote: 'Credit notes',
  Adjustment: 'Adjustments',
  Refund: 'Refunds',
  Payment: 'Payment reversals',
  PaymentAllocation: 'Allocation reversals',
  CreditNoteAllocation: 'Reversals of applied credit',
  PrefixChange: 'Prefix changes',
  OpeningBatch: 'Opening balances',
  CashierShift: 'Shift closings',
};

/**
 * Where a request's document is shown: its own page, or for an allocation or an application of credit the page of the
 * payment or the credit note it belongs to (`parentDocumentId`, ADR 0042 point 2). A prefix change, an opening batch and the closing of a shift have no page yet.
 */
export function documentLink(request: PendingApproval): { to: string; label: string } | null {
  const parent = request.parentDocumentId;
  switch (request.documentType) {
    case 'Bill':
      return { to: `/bills/${request.documentId}`, label: 'The bill' };
    case 'CreditNote':
      return { to: `/credit-notes/${request.documentId}`, label: 'The credit note' };
    case 'Adjustment':
      return { to: `/adjustments/${request.documentId}`, label: 'The adjustment' };
    case 'Refund':
      return { to: `/refunds/${request.documentId}`, label: 'The refund' };
    case 'Payment':
      return { to: `/payments/${request.documentId}`, label: 'The payment' };
    case 'PaymentAllocation':
      return parent === null ? null : { to: `/payments/${parent}`, label: 'Its payment' };
    case 'CreditNoteAllocation':
      return parent === null ? null : { to: `/credit-notes/${parent}`, label: 'Its credit note' };
    case 'PrefixChange':
    case 'OpeningBatch':
    case 'CashierShift':
      return null;
  }
}

export interface ApprovalListParams {
  documentType: ApprovalDocumentType | undefined;
  page: number;
  /** `''` the oldest first, as the inbox is by default; `-requestedAtUtc` the newest first (Prompt 12.2). */
  sort?: '' | '-requestedAtUtc';
}

export const approvalKeys = {
  all: ['approvals'] as const,
  list: (params: ApprovalListParams) => ['approvals', 'list', params] as const,
  review: (requestId: string) => ['approvals', 'review', requestId] as const,
};

/** After a decision: the inbox, and every list, document, statement and trail it may have changed. */
export function decisionsChanged(queryClient: QueryClient): void {
  for (const key of [
    ['approvals'],
    ['bills'],
    ['credit-notes'],
    ['adjustments'],
    ['refunds'],
    ['payments'],
    ['statement'],
    ['payer-open-bills'],
    ['audit'],
  ]) {
    void queryClient.invalidateQueries({ queryKey: key });
  }
}

export function usePendingApprovals(params: ApprovalListParams) {
  const client = useApiClient();
  return useQuery({
    queryKey: approvalKeys.list(params),
    queryFn: () =>
      answer(
        client.GET('/api/v1/reports/pending-approvals', {
          params: {
            query: {
              ...(params.documentType === undefined ? {} : { DocumentType: params.documentType }),
              ...(params.sort === undefined || params.sort === '' ? {} : { Sort: params.sort }),
              Page: params.page,
              PageSize: pageSize,
            },
          },
        }),
      ),
  });
}

/**
 * A document as a checker decides it (ADR 0042 point 3): what they see of it, and the two decisions, each sent with the
 * version just read and the key of its operation. The state is among the facts, so a conflict says when someone else
 * decided first.
 */
export interface Decidable {
  /** The path the decisions are sent to, for the identity of the operation. */
  path: string;
  version: string;
  facts: (readonly [string, string])[];
  approve: (comment: string, key: string) => Promise<unknown>;
  reject: (comment: string, key: string) => Promise<unknown>;
}

/** A difference of a count in words: a shortage or an excess, never a bare sign. */
export function differenceText(difference: number | string): string {
  const text = String(difference);
  if (Number(text) === 0) {
    return 'None';
  }
  const short = text.startsWith('-');
  return `${short ? 'Short by' : 'Over by'} ₹${formatDecimal(short ? text.slice(1) : text)}`;
}

const keyed = (version: string, key: string) => ({ ...ifMatch(version), 'Idempotency-Key': key });
const optional = (comment: string) => ({ comment: comment.trim() === '' ? null : comment.trim() });

/** Reads the document a request waits on, as it is now, and how to decide it. */
export async function readDecidable(client: ApiClient, request: PendingApproval): Promise<Decidable> {
  const id = request.documentId;
  switch (request.documentType) {
    case 'Bill': {
      const bill = await answer(client.GET('/api/v1/bills/{id}', { params: { path: { id } } }));
      const path = { params: { path: { id } } };
      return {
        path: `/bills/${id}`,
        version: bill.version,
        facts: [
          ['State', billStatusLabels[bill.status]],
          ['Bill date', formatDate(bill.billDate)],
          ['Lines', String(bill.lines.length)],
          ['Total', `₹${formatDecimal(bill.total)}`],
        ],
        approve: (comment, key) =>
          answer(
            client.POST('/api/v1/bills/{id}/approve', {
              ...path,
              body: optional(comment),
              headers: keyed(bill.version, key),
            }),
          ),
        reject: (comment, key) =>
          answer(
            client.POST('/api/v1/bills/{id}/reject', { ...path, body: { comment }, headers: keyed(bill.version, key) }),
          ),
      };
    }

    case 'CreditNote': {
      const note = await answer(client.GET('/api/v1/credit-notes/{id}', { params: { path: { id } } }));
      const path = { params: { path: { id } } };
      return {
        path: `/credit-notes/${id}`,
        version: note.version,
        facts: [
          ['State', creditNoteStatusLabels[note.status].label],
          ['Credit note date', formatDate(note.creditNoteDate)],
          ['Reason', note.reason],
          ['Lines', String(note.lines.length)],
          ['Total', `₹${formatDecimal(note.total)}`],
        ],
        approve: (comment, key) =>
          answer(
            client.POST('/api/v1/credit-notes/{id}/approve', {
              ...path,
              body: optional(comment),
              headers: keyed(note.version, key),
            }),
          ),
        reject: (comment, key) =>
          answer(
            client.POST('/api/v1/credit-notes/{id}/reject', {
              ...path,
              body: { comment },
              headers: keyed(note.version, key),
            }),
          ),
      };
    }

    case 'Adjustment': {
      const adjustment = await answer(client.GET('/api/v1/adjustments/{id}', { params: { path: { id } } }));
      const path = { params: { path: { id } } };
      return {
        path: `/adjustments/${id}`,
        version: adjustment.version,
        facts: [
          ['State', adjustmentStatusLabels[adjustment.status].label],
          ['Kind', kindLabels[adjustment.kind]],
          ['Adjustment date', formatDate(adjustment.adjustmentDate)],
          ['Reason', adjustment.reason],
          ['Amount', `₹${formatDecimal(adjustment.amount)}`],
        ],
        approve: (comment, key) =>
          answer(
            client.POST('/api/v1/adjustments/{id}/approve', {
              ...path,
              body: optional(comment),
              headers: keyed(adjustment.version, key),
            }),
          ),
        reject: (comment, key) =>
          answer(
            client.POST('/api/v1/adjustments/{id}/reject', {
              ...path,
              body: { comment },
              headers: keyed(adjustment.version, key),
            }),
          ),
      };
    }

    case 'Refund': {
      const refund = await answer(client.GET('/api/v1/refunds/{id}', { params: { path: { id } } }));
      const path = { params: { path: { id } } };
      return {
        path: `/refunds/${id}`,
        version: refund.version,
        facts: [
          ['State', refundStatusLabels[refund.status].label],
          ['From', refund.sourceKind === 'Payment' ? 'A payment' : 'A credit note'],
          ['Method', methodLabels[refund.method]],
          ['Reference', refund.reference ?? ''],
          ['Reason', refund.reason],
          ['Amount', `₹${formatDecimal(refund.amount)}`],
        ],
        approve: (comment, key) =>
          answer(
            client.POST('/api/v1/refunds/{id}/approve', {
              ...path,
              body: optional(comment),
              headers: keyed(refund.version, key),
            }),
          ),
        reject: (comment, key) =>
          answer(
            client.POST('/api/v1/refunds/{id}/reject', {
              ...path,
              body: { comment },
              headers: keyed(refund.version, key),
            }),
          ),
      };
    }

    case 'Payment': {
      const { payment } = await answer(client.GET('/api/v1/payments/{id}/allocations', { params: { path: { id } } }));
      const path = { params: { path: { id } } };
      return {
        path: `/payments/${id}`,
        version: payment.version,
        facts: [
          ['State', paymentStatusLabels[payment.status].label],
          ['Payment', payment.number],
          ['Payment date', formatDate(payment.paymentDate)],
          ['Method', methodLabels[payment.method]],
          ['Amount', `₹${formatDecimal(payment.amount)}`],
        ],
        approve: (comment, key) =>
          answer(
            client.POST('/api/v1/payments/{id}/approve-reversal', {
              ...path,
              body: optional(comment),
              headers: keyed(payment.version, key),
            }),
          ),
        reject: (comment, key) =>
          answer(
            client.POST('/api/v1/payments/{id}/reject-reversal', {
              ...path,
              body: { comment },
              headers: keyed(payment.version, key),
            }),
          ),
      };
    }

    case 'PaymentAllocation': {
      const paymentId = parentOf(request);
      const { payment, allocations } = await answer(
        client.GET('/api/v1/payments/{id}/allocations', { params: { path: { id: paymentId } } }),
      );
      const allocation = allocations.find((candidate) => candidate.id === id);
      const path = { params: { path: { id: paymentId, allocationId: id } } };
      return {
        path: `/payments/${paymentId}/allocations/${id}`,
        version: payment.version,
        facts: [
          ['State', allocation === undefined ? 'Not found' : allocationStatusLabels[allocation.status].label],
          ['Payment', payment.number],
          ['Amount applied', allocation === undefined ? '' : `₹${formatDecimal(allocation.amount)}`],
        ],
        approve: (comment, key) =>
          answer(
            client.POST('/api/v1/payments/{id}/allocations/{allocationId}/approve-reversal', {
              ...path,
              body: optional(comment),
              headers: keyed(payment.version, key),
            }),
          ),
        reject: (comment, key) =>
          answer(
            client.POST('/api/v1/payments/{id}/allocations/{allocationId}/reject-reversal', {
              ...path,
              body: { comment },
              headers: keyed(payment.version, key),
            }),
          ),
      };
    }

    case 'CreditNoteAllocation': {
      const creditNoteId = parentOf(request);
      const note = await answer(client.GET('/api/v1/credit-notes/{id}', { params: { path: { id: creditNoteId } } }));
      const application = note.applications.find((candidate) => candidate.id === id);
      const path = { params: { path: { id: creditNoteId, applicationId: id } } };
      return {
        path: `/credit-notes/${creditNoteId}/applications/${id}`,
        version: note.version,
        facts: [
          ['State', application === undefined ? 'Not found' : applicationStatusLabels[application.status].label],
          ['Credit note', note.number ?? ''],
          ['Amount applied', application === undefined ? '' : `₹${formatDecimal(application.amount)}`],
        ],
        approve: (comment, key) =>
          answer(
            client.POST('/api/v1/credit-notes/{id}/applications/{applicationId}/approve-reversal', {
              ...path,
              body: optional(comment),
              headers: keyed(note.version, key),
            }),
          ),
        reject: (comment, key) =>
          answer(
            client.POST('/api/v1/credit-notes/{id}/applications/{applicationId}/reject-reversal', {
              ...path,
              body: { comment },
              headers: keyed(note.version, key),
            }),
          ),
      };
    }

    case 'PrefixChange': {
      // A prefix change takes no Idempotency-Key: If-Match and the one waiting change per type refuse a repeat (ADR 0033).
      const change = await answer(client.GET('/api/v1/settings/prefix-changes/{id}', { params: { path: { id } } }));
      const path = { params: { path: { id } } };
      return {
        path: `/settings/prefix-changes/${id}`,
        version: change.version,
        facts: [
          ['State', change.status === 'Pending' ? 'Waiting for approval' : change.status],
          ['Documents', change.documentType],
          ['Prefix', `${change.fromPrefix} → ${change.toPrefix}`],
        ],
        approve: (comment) =>
          answer(
            client.POST('/api/v1/settings/prefix-changes/{id}/approve', {
              ...path,
              body: optional(comment),
              headers: ifMatch(change.version),
            }),
          ),
        reject: (comment) =>
          answer(
            client.POST('/api/v1/settings/prefix-changes/{id}/reject', {
              ...path,
              body: { comment },
              headers: ifMatch(change.version),
            }),
          ),
      };
    }

    case 'CashierShift': {
      // The closing of a shift (ADR 0060) posts nothing and takes no Idempotency-Key: If-Match refuses a repeat.
      const shift = await answer(client.GET('/api/v1/cashier-shifts/{id}', { params: { path: { id } } }));
      const path = { params: { path: { id } } };
      return {
        path: `/cashier-shifts/${id}`,
        version: shift.version,
        facts: [
          ['State', shift.status === 'PendingApproval' ? 'Waiting for approval' : shift.status],
          ['Cashier', shift.cashierName ?? ''],
          ['Business date', formatDate(shift.businessDate)],
          ['Expected cash', `₹${formatDecimal(shift.expectedCash)}`],
          ['Counted cash', shift.countedCash === null ? '' : `₹${formatDecimal(shift.countedCash)}`],
          ['Difference', shift.difference === null ? '' : differenceText(shift.difference)],
          ['Reason', shift.differenceReason ?? ''],
        ],
        approve: (comment) =>
          answer(
            client.POST('/api/v1/cashier-shifts/{id}/approve', {
              ...path,
              body: optional(comment),
              headers: ifMatch(shift.version),
            }),
          ),
        reject: (comment) =>
          answer(
            client.POST('/api/v1/cashier-shifts/{id}/reject', {
              ...path,
              body: { comment },
              headers: ifMatch(shift.version),
            }),
          ),
      };
    }

    case 'OpeningBatch': {
      // An opening batch (ADR 0053): its approval posts every opening receivable and advance, so it carries a key.
      const batch = await answer(client.GET('/api/v1/opening-batches/{id}', { params: { path: { id } } }));
      const path = { params: { path: { id } } };
      return {
        path: `/opening-batches/${id}`,
        version: batch.version,
        facts: [
          ['State', batch.status === 'PendingApproval' ? 'Waiting for approval' : batch.status],
          ['Cut-over date', formatDate(batch.cutOverDate)],
          ['Opening receivables', `${String(batch.receivables)}, ₹${formatDecimal(batch.receivableTotal)}`],
          ['Opening advances', `${String(batch.advances)}, ₹${formatDecimal(batch.advanceTotal)}`],
        ],
        approve: (comment, key) =>
          answer(
            client.POST('/api/v1/opening-batches/{id}/approve', {
              ...path,
              body: optional(comment),
              headers: keyed(batch.version, key),
            }),
          ),
        reject: (comment, key) =>
          answer(
            client.POST('/api/v1/opening-batches/{id}/reject', {
              ...path,
              body: { comment },
              headers: keyed(batch.version, key),
            }),
          ),
      };
    }
  }
}

function parentOf(request: PendingApproval): string {
  if (request.parentDocumentId === null) {
    throw new Error(`The ${request.documentType} ${request.documentId} came without the document it belongs to.`);
  }

  return request.parentDocumentId;
}
