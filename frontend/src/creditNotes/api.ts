import { useQuery, type QueryClient } from '@tanstack/react-query';
import { answer, answerWithStatus, ifMatch } from '../api/call';
import type { ApiClient } from '../api/client';
import { useApiClient } from '../api/ApiClientContext';
import type { components } from '../api/schema';
import type { BillLine } from '../bills/api';
import { documentStatuses, type StatusLook } from '../components/documentStatuses';
import type { ComparedField } from '../components/conflict';
import { pageSize } from '../components/listParams';
import { toNumber } from '../decimals';
import { formatDate, formatDecimal } from '../format';

/** A credit note as the API answers one (`docs/api.md` section 8.8). */
export type CreditNote = components['schemas']['CreditNoteDto'];
export type CreditNoteSummary = components['schemas']['CreditNoteSummaryDto'];
export type CreditNoteLine = components['schemas']['CreditNoteLineDto'];
export type CreditNoteStatus = components['schemas']['CreditNoteStatus'];
export type CreditApplication = components['schemas']['CreditNoteApplicationDto'];

export const creditNoteStatuses: readonly CreditNoteStatus[] = ['Draft', 'PendingApproval', 'Posted', 'Voided'];

export const creditNoteStatusLabels: Record<CreditNoteStatus, StatusLook> = documentStatuses.creditNote;

export const applicationStatusLabels: Record<CreditApplication['status'], StatusLook> =
  documentStatuses.creditApplication;

/**
 * A line as the editor holds it (ADR 0042 point 4): one that names a line of the bill takes its description, price and
 * tax rate from it, which the editor shows and never sends; a free-text line is typed in full. Numbers are text.
 */
export interface CreditNoteLineValues {
  billLineId: string | null;
  description: string;
  quantity: string;
  unitPrice: string;
  discountAmount: string;
  taxRatePercent: string;
}

export interface CreditNoteValues {
  creditNoteDate: string;
  reason: string;
  lines: CreditNoteLineValues[];
}

export const headerFields = ['creditNoteDate', 'reason'] as const;

/** A line of the bill to credit: all of it at first (its quantity and its discount), which the user may lower. */
export function lineFromBill(line: BillLine): CreditNoteLineValues {
  return {
    billLineId: line.id,
    description: line.description,
    quantity: String(line.quantity),
    unitPrice: String(line.unitPrice),
    discountAmount: Number(line.discountAmount) === 0 ? '' : String(line.discountAmount),
    taxRatePercent: String(line.taxRatePercent),
  };
}

export const emptyLine: CreditNoteLineValues = {
  billLineId: null,
  description: '',
  quantity: '1',
  unitPrice: '',
  discountAmount: '',
  taxRatePercent: '0',
};

export function lineValues(line: CreditNoteLine): CreditNoteLineValues {
  return {
    billLineId: line.billLineId,
    description: line.description,
    quantity: String(line.quantity),
    unitPrice: String(line.unitPrice),
    discountAmount: Number(line.discountAmount) === 0 ? '' : String(line.discountAmount),
    taxRatePercent: String(line.taxRatePercent),
  };
}

export function creditNoteValues(creditNote: CreditNote): CreditNoteValues {
  return {
    creditNoteDate: creditNote.creditNoteDate,
    reason: creditNote.reason,
    lines: creditNote.lines.map(lineValues),
  };
}

/** A line as the API takes it: a line of the bill by its id, the quantity and the discount; a free line in full. */
export function lineRequest(line: CreditNoteLineValues) {
  const discountAmount = line.discountAmount.trim() === '' ? 0 : toNumber(line.discountAmount);
  return line.billLineId !== null
    ? {
        billLineId: line.billLineId,
        description: null,
        quantity: toNumber(line.quantity),
        unitPrice: null,
        discountAmount,
        taxRatePercent: null,
      }
    : {
        billLineId: null,
        description: line.description.trim(),
        quantity: toNumber(line.quantity),
        unitPrice: toNumber(line.unitPrice),
        discountAmount,
        taxRatePercent: toNumber(line.taxRatePercent),
      };
}

export function headerRequest(values: CreditNoteValues) {
  return { creditNoteDate: values.creditNoteDate, reason: values.reason.trim() };
}

export function newCreditNoteRequest(billId: string, values: CreditNoteValues) {
  return {
    billId,
    creditNoteDate: values.creditNoteDate.trim() === '' ? null : values.creditNoteDate,
    reason: values.reason.trim(),
    lines: values.lines.map(lineRequest),
  };
}

export function headerChanged(values: CreditNoteValues, creditNote: CreditNote): boolean {
  return JSON.stringify(headerRequest(values)) !== JSON.stringify(headerRequest(creditNoteValues(creditNote)));
}

export function linesChanged(values: CreditNoteValues, creditNote: CreditNote): boolean {
  return (
    JSON.stringify(values.lines.map(lineRequest)) !== JSON.stringify(creditNote.lines.map(lineValues).map(lineRequest))
  );
}

/** A field of the API's answer named by the position of a line (`lines[1].quantity`), as the editor names it. */
export function lineField(apiField: string): `lines.${number}.${keyof CreditNoteLineValues}` | null {
  const match = /^lines\[(\d+)\]\.(\w+)$/i.exec(apiField);
  if (match === null) {
    return null;
  }

  const [, index = '0', name = ''] = match;
  const field = (name.charAt(0).toLowerCase() + name.slice(1)) as keyof CreditNoteLineValues;
  const known: readonly string[] = [
    'billLineId',
    'description',
    'quantity',
    'unitPrice',
    'discountAmount',
    'taxRatePercent',
  ];
  return known.includes(field) ? `lines.${Number(index)}.${field}` : null;
}

const lineSummary = (creditNote: CreditNote) =>
  creditNote.lines.length === 0
    ? 'none'
    : creditNote.lines.map((line) => `${line.description} × ${String(line.quantity)}`).join('; ');

/** The fields a conflict compares (ADR 0039 point 7). */
export const comparedCreditNoteFields: readonly ComparedField<CreditNote>[] = [
  { label: 'Credit note date', value: (creditNote) => formatDate(creditNote.creditNoteDate) },
  { label: 'Reason', value: (creditNote) => creditNote.reason },
  { label: 'Lines', value: lineSummary },
  { label: 'Total', value: (creditNote) => formatDecimal(creditNote.total) },
  { label: 'Available credit', value: (creditNote) => formatDecimal(creditNote.availableAmount) },
  {
    label: 'Applications',
    value: (creditNote) =>
      creditNote.applications
        .map(
          (application) => `${formatDecimal(application.amount)} ${applicationStatusLabels[application.status].label}`,
        )
        .join('; '),
  },
  { label: 'Status', value: (creditNote) => creditNoteStatusLabels[creditNote.status].label },
];

export interface CreditNoteListParams {
  status: CreditNoteStatus | undefined;
  billId: string;
  payerId: string;
  page: number;
}

export const creditNoteKeys = {
  all: ['credit-notes'] as const,
  list: (params: CreditNoteListParams) => ['credit-notes', 'list', params] as const,
  one: (id: string) => ['credit-notes', id] as const,
};

/** After a credit note changed: the lists, the bills it touched, the statements, the inbox and the trails. */
export function creditNotesChanged(queryClient: QueryClient): void {
  for (const key of [
    ['credit-notes', 'list'],
    ['bills'],
    ['statement'],
    ['payer-open-bills'],
    ['approvals'],
    ['audit'],
  ]) {
    void queryClient.invalidateQueries({ queryKey: key });
  }
}

export function getCreditNote(client: ApiClient, id: string): Promise<CreditNote> {
  return answer(client.GET('/api/v1/credit-notes/{id}', { params: { path: { id } } }));
}

export function createCreditNote(
  client: ApiClient,
  body: ReturnType<typeof newCreditNoteRequest>,
  key: string,
): Promise<CreditNote> {
  return answer(client.POST('/api/v1/credit-notes', { body, headers: { 'Idempotency-Key': key } }));
}

export function correctHeader(
  client: ApiClient,
  creditNote: CreditNote,
  values: CreditNoteValues,
): Promise<CreditNote> {
  return answer(
    client.PATCH('/api/v1/credit-notes/{id}', {
      params: { path: { id: creditNote.id } },
      body: headerRequest(values),
      headers: ifMatch(creditNote.version),
    }),
  );
}

export function replaceLines(client: ApiClient, creditNote: CreditNote, values: CreditNoteValues): Promise<CreditNote> {
  return answer(
    client.PUT('/api/v1/credit-notes/{id}/lines', {
      params: { path: { id: creditNote.id } },
      body: { lines: values.lines.map(lineRequest) },
      headers: ifMatch(creditNote.version),
    }),
  );
}

export function voidCreditNote(
  client: ApiClient,
  creditNote: CreditNote,
  reason: string,
  key: string,
): Promise<{ data: CreditNote; status: number }> {
  return answerWithStatus(
    client.POST('/api/v1/credit-notes/{id}/void', {
      params: { path: { id: creditNote.id } },
      body: { reason },
      headers: { ...ifMatch(creditNote.version), 'Idempotency-Key': key },
    }),
  );
}

/** Hands a draft to a checker (BR-212, BR-604): always 202; the checker's approval is the posting. */
export function submitCreditNote(
  client: ApiClient,
  creditNote: CreditNote,
  key: string,
): Promise<{ data: CreditNote; status: number }> {
  return answerWithStatus(
    client.POST('/api/v1/credit-notes/{id}/post', {
      params: { path: { id: creditNote.id } },
      headers: { ...ifMatch(creditNote.version), 'Idempotency-Key': key },
    }),
  );
}

/** Applies a posted credit note's available credit to bills of its payer (BR-213), every line or none. */
export function applyCredit(
  client: ApiClient,
  creditNote: CreditNote,
  lines: readonly { billId: string; amount: string }[],
  key: string,
): Promise<CreditNote> {
  return answer(
    client.POST('/api/v1/credit-notes/{id}/applications', {
      params: { path: { id: creditNote.id } },
      body: { applications: lines.map((line) => ({ billId: line.billId, amount: Number(line.amount) })) },
      headers: { ...ifMatch(creditNote.version), 'Idempotency-Key': key },
    }),
  );
}

/** Asks for the reversal of a later application of credit (ADR 0028): 202, the application waits for a checker. */
export function requestApplicationReversal(
  client: ApiClient,
  creditNote: CreditNote,
  application: CreditApplication,
  reason: string,
  key: string,
): Promise<{ data: CreditNote; status: number }> {
  return answerWithStatus(
    client.POST('/api/v1/credit-notes/{id}/applications/{applicationId}/reverse', {
      params: { path: { id: creditNote.id, applicationId: application.id } },
      body: { reason },
      headers: { ...ifMatch(creditNote.version), 'Idempotency-Key': key },
    }),
  );
}

export function useCreditNotes(params: CreditNoteListParams) {
  const client = useApiClient();
  return useQuery({
    queryKey: creditNoteKeys.list(params),
    queryFn: () =>
      answer(
        client.GET('/api/v1/credit-notes', {
          params: {
            query: {
              ...(params.status === undefined ? {} : { Status: params.status }),
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

/** A credit note; with `enabled` false it reads nothing (a refund drawn from a payment). */
export function useCreditNote(id: string, enabled = true) {
  const client = useApiClient();
  return useQuery({ queryKey: creditNoteKeys.one(id), queryFn: () => getCreditNote(client, id), enabled });
}
