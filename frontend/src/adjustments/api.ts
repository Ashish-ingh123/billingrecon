import { useQuery, type QueryClient } from '@tanstack/react-query';
import { answer, answerWithStatus, ifMatch } from '../api/call';
import type { ApiClient } from '../api/client';
import { useApiClient } from '../api/ApiClientContext';
import type { components } from '../api/schema';
import type { Tone } from '../components/Badge';
import type { ComparedField } from '../components/conflict';
import { pageSize } from '../components/listParams';
import { formatDate, formatDecimal } from '../format';
import type { Paise } from '../bills/lineMath';
import { checkAmount, paiseOf } from '../money';

/** An adjustment as the API answers one (`docs/api.md` section 8.9). */
export type Adjustment = components['schemas']['AdjustmentDto'];
export type AdjustmentKind = components['schemas']['AdjustmentKind'];
export type AdjustmentStatus = components['schemas']['AdjustmentStatus'];

export const adjustmentKinds: readonly AdjustmentKind[] = ['Discount', 'WriteOff', 'Rounding'];

export const kindLabels: Record<AdjustmentKind, string> = {
  Discount: 'Discount',
  WriteOff: 'Write-off',
  Rounding: 'Rounding',
};

export const adjustmentStatuses: readonly AdjustmentStatus[] = ['Draft', 'PendingApproval', 'Posted', 'Voided'];

export const adjustmentStatusLabels: Record<AdjustmentStatus, { label: string; tone: Tone }> = {
  Draft: { label: 'Draft', tone: 'muted' },
  PendingApproval: { label: 'Waiting for approval', tone: 'warning' },
  Posted: { label: 'Posted', tone: 'accent' },
  Voided: { label: 'Voided', tone: 'danger' },
};

/** What an adjustment form holds, as text; the bill is fixed once the draft exists. */
export interface AdjustmentValues {
  adjustmentDate: string;
  kind: AdjustmentKind | '';
  amount: string;
  reason: string;
}

export const adjustmentFields = ['adjustmentDate', 'kind', 'amount', 'reason'] as const;

/** A rounding is less than one rupee (BR-502): at most 99 paise. */
const mostRounding: Paise = 99n;

/**
 * The message for an adjustment's amount (BR-501, BR-502): above zero, two decimals, not more than what the bill still
 * owes, and less than one rupee for a rounding; or true.
 */
export function checkAdjustmentAmount(
  text: string,
  kind: AdjustmentKind | '',
  balanceDue: Paise,
  balanceText: string,
): true | string {
  const checked = checkAmount(text, balanceDue, `the bill's balance due, ₹${balanceText}`);
  if (checked !== true) {
    return checked;
  }

  const paise = paiseOf(text) ?? 0n;
  return kind !== 'Rounding' || paise <= mostRounding || 'A rounding is less than ₹1.00.';
}

export function adjustmentValues(adjustment: Adjustment): AdjustmentValues {
  return {
    adjustmentDate: adjustment.adjustmentDate,
    kind: adjustment.kind,
    amount: Number(adjustment.amount).toFixed(2),
    reason: adjustment.reason,
  };
}

function adjustmentBody(values: AdjustmentValues) {
  return {
    adjustmentDate: values.adjustmentDate.trim() === '' ? null : values.adjustmentDate,
    kind: values.kind === '' ? null : values.kind,
    amount: Number(values.amount.trim()),
    reason: values.reason.trim(),
  };
}

export function draftAdjustmentRequest(billId: string, values: AdjustmentValues) {
  return { billId, ...adjustmentBody(values) };
}

export const comparedAdjustmentFields: readonly ComparedField<Adjustment>[] = [
  { label: 'Adjustment date', value: (adjustment) => formatDate(adjustment.adjustmentDate) },
  { label: 'Kind', value: (adjustment) => kindLabels[adjustment.kind] },
  { label: 'Amount', value: (adjustment) => formatDecimal(adjustment.amount) },
  { label: 'Reason', value: (adjustment) => adjustment.reason },
  { label: 'Status', value: (adjustment) => adjustmentStatusLabels[adjustment.status].label },
];

export interface AdjustmentListParams {
  status: AdjustmentStatus | undefined;
  kind: AdjustmentKind | undefined;
  billId: string;
  payerId: string;
  page: number;
}

export const adjustmentKeys = {
  all: ['adjustments'] as const,
  list: (params: AdjustmentListParams) => ['adjustments', 'list', params] as const,
  one: (id: string) => ['adjustments', id] as const,
};

/** After an adjustment changed: the lists, the bills, the statements, the inbox and the trails are read again. */
export function adjustmentsChanged(queryClient: QueryClient): void {
  for (const key of [
    ['adjustments', 'list'],
    ['bills'],
    ['statement'],
    ['payer-open-bills'],
    ['approvals'],
    ['audit'],
  ]) {
    void queryClient.invalidateQueries({ queryKey: key });
  }
}

export function getAdjustment(client: ApiClient, id: string): Promise<Adjustment> {
  return answer(client.GET('/api/v1/adjustments/{id}', { params: { path: { id } } }));
}

export function draftAdjustment(
  client: ApiClient,
  body: ReturnType<typeof draftAdjustmentRequest>,
  key: string,
): Promise<Adjustment> {
  return answer(client.POST('/api/v1/adjustments', { body, headers: { 'Idempotency-Key': key } }));
}

/** Replaces a draft as a whole (`PUT`), with the version it started from. */
export function correctAdjustment(
  client: ApiClient,
  adjustment: Adjustment,
  values: AdjustmentValues,
): Promise<Adjustment> {
  return answer(
    client.PUT('/api/v1/adjustments/{id}', {
      params: { path: { id: adjustment.id } },
      body: adjustmentBody(values),
      headers: ifMatch(adjustment.version),
    }),
  );
}

export function voidAdjustment(
  client: ApiClient,
  adjustment: Adjustment,
  reason: string,
  key: string,
): Promise<{ data: Adjustment; status: number }> {
  return answerWithStatus(
    client.POST('/api/v1/adjustments/{id}/void', {
      params: { path: { id: adjustment.id } },
      body: { reason },
      headers: { ...ifMatch(adjustment.version), 'Idempotency-Key': key },
    }),
  );
}

/** Hands a draft to a checker (BR-604; ADR 0026, a rounding included): 202, the approval is the posting. */
export function submitAdjustment(
  client: ApiClient,
  adjustment: Adjustment,
  key: string,
): Promise<{ data: Adjustment; status: number }> {
  return answerWithStatus(
    client.POST('/api/v1/adjustments/{id}/post', {
      params: { path: { id: adjustment.id } },
      headers: { ...ifMatch(adjustment.version), 'Idempotency-Key': key },
    }),
  );
}

export function useAdjustments(params: AdjustmentListParams) {
  const client = useApiClient();
  return useQuery({
    queryKey: adjustmentKeys.list(params),
    queryFn: () =>
      answer(
        client.GET('/api/v1/adjustments', {
          params: {
            query: {
              ...(params.status === undefined ? {} : { Status: params.status }),
              ...(params.kind === undefined ? {} : { Kind: params.kind }),
              ...(params.billId === '' ? {} : { BillId: params.billId }),
              ...(params.payerId === '' ? {} : { PayerId: params.payerId }),
              Page: params.page,
              PageSize: pageSize,
            },
          },
        }),
      ),
  });
}

export function useAdjustment(id: string) {
  const client = useApiClient();
  return useQuery({ queryKey: adjustmentKeys.one(id), queryFn: () => getAdjustment(client, id) });
}
