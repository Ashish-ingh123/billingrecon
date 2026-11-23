import { useQuery, type QueryClient } from '@tanstack/react-query';
import { answer, answerWithStatus, ifMatch } from '../api/call';
import type { ApiClient } from '../api/client';
import { useApiClient } from '../api/ApiClientContext';
import type { components } from '../api/schema';
import type { Tone } from '../components/Badge';
import type { ComparedField } from '../components/conflict';
import { pageSize } from '../components/listParams';
import { formatDate, formatDecimal } from '../format';
import { methodLabels, type PaymentMethod } from '../payments/api';

/** A refund as the API answers one (`docs/api.md` section 8.10). */
export type Refund = components['schemas']['RefundDto'];
export type RefundStatus = components['schemas']['RefundStatus'];
export type CreditNote = components['schemas']['CreditNoteDto'];

export const refundStatuses: readonly RefundStatus[] = ['Draft', 'PendingApproval', 'Posted', 'Voided'];

export const refundStatusLabels: Record<RefundStatus, { label: string; tone: Tone }> = {
  Draft: { label: 'Draft', tone: 'muted' },
  PendingApproval: { label: 'Waiting for approval', tone: 'warning' },
  Posted: { label: 'Posted', tone: 'accent' },
  Voided: { label: 'Voided', tone: 'danger' },
};

/** What a refund form holds, as text: the source is fixed once the draft exists. */
export interface RefundValues {
  refundDate: string;
  amount: string;
  method: PaymentMethod | '';
  reference: string;
  reason: string;
}

export const refundFields = ['refundDate', 'amount', 'method', 'reference', 'reason'] as const;

export function refundValues(refund: Refund): RefundValues {
  return {
    refundDate: refund.refundDate,
    amount: Number(refund.amount).toFixed(2),
    method: refund.method,
    reference: refund.reference ?? '',
    reason: refund.reason,
  };
}

function refundBody(values: RefundValues) {
  return {
    refundDate: values.refundDate.trim() === '' ? null : values.refundDate,
    amount: Number(values.amount.trim()),
    method: values.method === '' ? null : values.method,
    reference: values.reference.trim() === '' ? null : values.reference.trim(),
    reason: values.reason.trim(),
  };
}

/** The source of a new refund (ADR 0041 point 2): a payment's unapplied cash or a credit note's available credit. */
export type RefundSource = { kind: 'Payment'; id: string } | { kind: 'CreditNote'; id: string };

export function draftRefundRequest(source: RefundSource, values: RefundValues) {
  return {
    paymentId: source.kind === 'Payment' ? source.id : null,
    creditNoteId: source.kind === 'CreditNote' ? source.id : null,
    ...refundBody(values),
  };
}

export const comparedRefundFields: readonly ComparedField<Refund>[] = [
  { label: 'Refund date', value: (refund) => formatDate(refund.refundDate) },
  { label: 'Amount', value: (refund) => formatDecimal(refund.amount) },
  { label: 'Method', value: (refund) => methodLabels[refund.method] },
  { label: 'Reference', value: (refund) => refund.reference ?? '' },
  { label: 'Reason', value: (refund) => refund.reason },
  { label: 'Status', value: (refund) => refundStatusLabels[refund.status].label },
];

export interface RefundListParams {
  status: RefundStatus | undefined;
  payerId: string;
  page: number;
}

export const refundKeys = {
  all: ['refunds'] as const,
  list: (params: RefundListParams) => ['refunds', 'list', params] as const,
  one: (id: string) => ['refunds', id] as const,
};

/** After a refund changed: the lists, the statements and the trails are read again. */
export function refundsChanged(queryClient: QueryClient): void {
  for (const key of [['refunds', 'list'], ['statement'], ['audit']]) {
    void queryClient.invalidateQueries({ queryKey: key });
  }
}

export function getRefund(client: ApiClient, id: string): Promise<Refund> {
  return answer(client.GET('/api/v1/refunds/{id}', { params: { path: { id } } }));
}

export function draftRefund(
  client: ApiClient,
  body: ReturnType<typeof draftRefundRequest>,
  key: string,
): Promise<Refund> {
  return answer(client.POST('/api/v1/refunds', { body, headers: { 'Idempotency-Key': key } }));
}

export function correctRefund(client: ApiClient, refund: Refund, values: RefundValues): Promise<Refund> {
  return answer(
    client.PUT('/api/v1/refunds/{id}', {
      params: { path: { id: refund.id } },
      body: refundBody(values),
      headers: ifMatch(refund.version),
    }),
  );
}

export function voidRefund(
  client: ApiClient,
  refund: Refund,
  reason: string,
  key: string,
): Promise<{ data: Refund; status: number }> {
  return answerWithStatus(
    client.POST('/api/v1/refunds/{id}/void', {
      params: { path: { id: refund.id } },
      body: { reason },
      headers: { ...ifMatch(refund.version), 'Idempotency-Key': key },
    }),
  );
}

/** Submits a draft for approval (BR-604): 202, the refund waits for a second person, who posts it. */
export function submitRefund(
  client: ApiClient,
  refund: Refund,
  key: string,
): Promise<{ data: Refund; status: number }> {
  return answerWithStatus(
    client.POST('/api/v1/refunds/{id}/post', {
      params: { path: { id: refund.id } },
      headers: { ...ifMatch(refund.version), 'Idempotency-Key': key },
    }),
  );
}

export function useRefunds(params: RefundListParams) {
  const client = useApiClient();
  return useQuery({
    queryKey: refundKeys.list(params),
    queryFn: () =>
      answer(
        client.GET('/api/v1/refunds', {
          params: {
            query: {
              ...(params.status === undefined ? {} : { Status: params.status }),
              ...(params.payerId === '' ? {} : { PayerId: params.payerId }),
              Page: params.page,
              PageSize: pageSize,
            },
          },
        }),
      ),
  });
}

export function useRefund(id: string) {
  const client = useApiClient();
  return useQuery({ queryKey: refundKeys.one(id), queryFn: () => getRefund(client, id) });
}

/** A credit note, the source of a refund; with `enabled` false it reads nothing (a refund drawn from a payment). */
export function useCreditNote(id: string, enabled = true) {
  const client = useApiClient();
  return useQuery({
    queryKey: ['credit-notes', id],
    queryFn: () => answer(client.GET('/api/v1/credit-notes/{id}', { params: { path: { id } } })),
    enabled,
  });
}
