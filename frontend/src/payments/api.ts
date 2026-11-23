import { useQuery, type QueryClient } from '@tanstack/react-query';
import { answer, answerWithStatus, ifMatch } from '../api/call';
import type { ApiClient } from '../api/client';
import { useApiClient } from '../api/ApiClientContext';
import type { components } from '../api/schema';
import type { Tone } from '../components/Badge';
import type { ComparedField } from '../components/conflict';
import { pageSize } from '../components/listParams';
import { formatDecimal } from '../format';

/** A payment as the API answers one (`docs/api.md` section 8.7). */
export type Payment = components['schemas']['PaymentDto'];
export type PaymentWithAllocations = components['schemas']['PaymentAllocationsDto'];
export type Allocation = components['schemas']['PaymentAllocationDto'];
export type PaymentMethod = components['schemas']['PaymentMethod'];
export type PaymentStatus = components['schemas']['PaymentStatus'];

export const paymentMethods: readonly PaymentMethod[] = ['Cash', 'Card', 'UPI', 'BankTransfer', 'Cheque'];

export const methodLabels: Record<PaymentMethod, string> = {
  Cash: 'Cash',
  Card: 'Card',
  UPI: 'UPI',
  BankTransfer: 'Bank transfer',
  Cheque: 'Cheque',
};

export const paymentStatuses: readonly PaymentStatus[] = ['Posted', 'PendingReversal', 'Reversed'];

export const paymentStatusLabels: Record<PaymentStatus, { label: string; tone: Tone }> = {
  Posted: { label: 'Posted', tone: 'accent' },
  PendingReversal: { label: 'Waiting for reversal', tone: 'warning' },
  Reversed: { label: 'Reversed', tone: 'danger' },
};

export const allocationStatusLabels: Record<Allocation['status'], { label: string; tone: Tone }> = {
  Active: { label: 'Applied', tone: 'success' },
  PendingReversal: { label: 'Waiting for reversal', tone: 'warning' },
  Reversed: { label: 'Reversed', tone: 'danger' },
};

/** The message for a reference: required for every method but cash (BR-301), at most 100 characters. */
export function checkReference(reference: string, method: PaymentMethod | ''): true | string {
  if (method !== 'Cash' && method !== '' && reference.trim() === '') {
    return 'Enter the reference: the UPI id, card authorisation, UTR or cheque number.';
  }

  return reference.length <= 100 || 'The reference is at most 100 characters.';
}

/** What a payer's payment form holds, as text. */
export interface PaymentValues {
  paymentDate: string;
  method: PaymentMethod | '';
  reference: string;
  amount: string;
  notes: string;
}

export const paymentFields = ['paymentDate', 'method', 'reference', 'amount', 'notes'] as const;

const blankAsNull = (value: string): string | null => (value.trim() === '' ? null : value);

export function recordPaymentRequest(payerId: string, values: PaymentValues) {
  return {
    payerId,
    paymentDate: blankAsNull(values.paymentDate),
    method: values.method === '' ? null : values.method,
    reference: values.method === 'Cash' ? blankAsNull(values.reference) : values.reference.trim(),
    amount: Number(values.amount.trim()),
    notes: blankAsNull(values.notes),
  };
}

/** The fields a conflict compares on a payment's page. */
export const comparedPaymentFields: readonly ComparedField<PaymentWithAllocations>[] = [
  { label: 'Status', value: (record) => paymentStatusLabels[record.payment.status].label },
  { label: 'Unapplied cash', value: (record) => formatDecimal(record.payment.unappliedAmount) },
  {
    label: 'Allocations',
    value: (record) =>
      record.allocations
        .map((allocation) => `${formatDecimal(allocation.amount)} ${allocationStatusLabels[allocation.status].label}`)
        .join('; '),
  },
];

export interface PaymentListParams {
  status: PaymentStatus | undefined;
  method: PaymentMethod | undefined;
  from: string;
  to: string;
  payerId: string;
  page: number;
}

export const paymentKeys = {
  all: ['payments'] as const,
  list: (params: PaymentListParams) => ['payments', 'list', params] as const,
  one: (id: string) => ['payments', id] as const,
};

/** After a payment changed: the lists, the statements, the bills it touched and their trails are read again. */
export function paymentsChanged(queryClient: QueryClient): void {
  for (const key of [['payments', 'list'], ['statement'], ['bills'], ['audit'], ['payer-open-bills']]) {
    void queryClient.invalidateQueries({ queryKey: key });
  }
}

export function getPaymentWithAllocations(client: ApiClient, id: string): Promise<PaymentWithAllocations> {
  return answer(client.GET('/api/v1/payments/{id}/allocations', { params: { path: { id } } }));
}

export function recordPayment(
  client: ApiClient,
  body: ReturnType<typeof recordPaymentRequest>,
  key: string,
): Promise<Payment> {
  return answer(client.POST('/api/v1/payments', { body, headers: { 'Idempotency-Key': key } }));
}

/** Applies the payment to bills (US-31, BR-303): every line or none, with the payment's version. */
export function allocate(
  client: ApiClient,
  payment: Payment,
  lines: readonly { billId: string; amount: string }[],
  key: string,
): Promise<{ data: PaymentWithAllocations; status: number }> {
  return answerWithStatus(
    client.POST('/api/v1/payments/{id}/allocations', {
      params: { path: { id: payment.id } },
      body: { allocations: lines.map((line) => ({ billId: line.billId, amount: Number(line.amount.trim()) })) },
      headers: { ...ifMatch(payment.version), 'Idempotency-Key': key },
    }),
  );
}

/** Asks for the reversal of a payment (US-33, BR-306): 202, the payment waits for a second person. */
export function requestPaymentReversal(
  client: ApiClient,
  payment: Payment,
  reason: string,
  key: string,
): Promise<{ data: PaymentWithAllocations; status: number }> {
  return answerWithStatus(
    client.POST('/api/v1/payments/{id}/reverse', {
      params: { path: { id: payment.id } },
      body: { reason },
      headers: { ...ifMatch(payment.version), 'Idempotency-Key': key },
    }),
  );
}

/** Asks for the reversal of one allocation (US-32, BR-305): 202, the allocation waits for a second person. */
export function requestAllocationReversal(
  client: ApiClient,
  payment: Payment,
  allocation: Allocation,
  reason: string,
  key: string,
): Promise<{ data: PaymentWithAllocations; status: number }> {
  return answerWithStatus(
    client.POST('/api/v1/payments/{id}/allocations/{allocationId}/reverse', {
      params: { path: { id: payment.id, allocationId: allocation.id } },
      body: { reason },
      headers: { ...ifMatch(payment.version), 'Idempotency-Key': key },
    }),
  );
}

export function usePayments(params: PaymentListParams) {
  const client = useApiClient();
  return useQuery({
    queryKey: paymentKeys.list(params),
    queryFn: () =>
      answer(
        client.GET('/api/v1/payments', {
          params: {
            query: {
              ...(params.status === undefined ? {} : { Status: params.status }),
              ...(params.method === undefined ? {} : { Method: params.method }),
              ...(params.from === '' ? {} : { PaymentDateFrom: params.from }),
              ...(params.to === '' ? {} : { PaymentDateTo: params.to }),
              ...(params.payerId === '' ? {} : { PayerId: params.payerId }),
              Page: params.page,
              PageSize: pageSize,
            },
          },
        }),
      ),
  });
}

/** A payment with its allocations; with `enabled` false it reads nothing (a refund drawn from a credit note). */
export function usePaymentWithAllocations(id: string, enabled = true) {
  const client = useApiClient();
  return useQuery({
    queryKey: paymentKeys.one(id),
    queryFn: () => getPaymentWithAllocations(client, id),
    enabled,
  });
}
