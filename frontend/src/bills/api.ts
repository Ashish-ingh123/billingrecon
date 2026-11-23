import { useQuery, type QueryClient } from '@tanstack/react-query';
import { answer, answerWithStatus, ifMatch } from '../api/call';
import type { ApiClient } from '../api/client';
import { useApiClient } from '../api/ApiClientContext';
import type { components } from '../api/schema';
import type { ComparedField } from '../components/conflict';
import { pageSize } from '../components/listParams';
import { toNumber } from '../decimals';
import { formatDate, formatDecimal } from '../format';

/** A bill with its lines as the API answers one (`docs/api.md` section 8.4). */
export type Bill = components['schemas']['BillDto'];
export type BillLine = components['schemas']['BillLineDto'];
export type BillStatus = components['schemas']['BillStatus'];
type LineRequest = components['schemas']['BillLineRequest'];

export const billStatuses: readonly BillStatus[] = [
  'Draft',
  'PendingApproval',
  'Posted',
  'PartiallyPaid',
  'Settled',
  'Voided',
];

export const billStatusLabels: Record<BillStatus, string> = {
  Draft: 'Draft',
  PendingApproval: 'Waiting for approval',
  Posted: 'Posted',
  PartiallyPaid: 'Partly paid',
  Settled: 'Settled',
  Voided: 'Voided',
};

/** A line as the editor holds it: the numbers as text, checked as text and sent as numbers (ADR 0039 point 5). */
export interface BillLineValues {
  serviceItemId: string | null;
  /** What the line came from, for the editor: the item's code, or empty for a free-text line. */
  itemCode: string;
  description: string;
  quantity: string;
  unitPrice: string;
  discountAmount: string;
  taxRatePercent: string;
}

/** What the editor holds of a draft: its header and its lines; the patient and the payer are chosen beside it. */
export interface BillValues {
  billDate: string;
  dueDate: string;
  encounterReference: string;
  notes: string;
  lines: BillLineValues[];
}

export const headerFields = ['billDate', 'dueDate', 'encounterReference', 'notes'] as const;

const blankAsNull = (value: string): string | null => (value.trim() === '' ? null : value);

export function lineValues(line: BillLine): BillLineValues {
  return {
    serviceItemId: line.serviceItemId,
    itemCode: '',
    description: line.description,
    quantity: String(line.quantity),
    unitPrice: String(line.unitPrice),
    discountAmount: Number(line.discountAmount) === 0 ? '' : String(line.discountAmount),
    taxRatePercent: String(line.taxRatePercent),
  };
}

export function billValues(bill: Bill): BillValues {
  return {
    billDate: bill.billDate,
    dueDate: bill.dueDate,
    encounterReference: bill.encounterReference ?? '',
    notes: bill.notes ?? '',
    lines: bill.lines.map(lineValues),
  };
}

/** A line as the API takes it: every number given, a blank discount as 0. */
export function lineRequest(line: BillLineValues): LineRequest {
  return {
    serviceItemId: line.serviceItemId,
    description: line.description,
    quantity: toNumber(line.quantity),
    unitPrice: toNumber(line.unitPrice),
    discountAmount: line.discountAmount.trim() === '' ? 0 : toNumber(line.discountAmount),
    taxRatePercent: toNumber(line.taxRatePercent),
  };
}

export function headerRequest(values: BillValues) {
  return {
    billDate: values.billDate,
    dueDate: values.dueDate,
    encounterReference: blankAsNull(values.encounterReference),
    notes: blankAsNull(values.notes),
  };
}

/** The body of a new draft (`POST /bills`): its parties, header and lines. A blank due date is the hospital's default. */
export function newBillRequest(patientId: string, payerId: string, values: BillValues) {
  return {
    patientId,
    payerId,
    billDate: blankAsNull(values.billDate),
    dueDate: blankAsNull(values.dueDate),
    encounterReference: blankAsNull(values.encounterReference),
    notes: blankAsNull(values.notes),
    lines: values.lines.map(lineRequest),
  };
}

/** Whether the header the editor holds differs from the bill's. */
export function headerChanged(values: BillValues, bill: Bill): boolean {
  return JSON.stringify(headerRequest(values)) !== JSON.stringify(headerRequest(billValues(bill)));
}

/** Whether the lines the editor holds differ from the bill's. */
export function linesChanged(values: BillValues, bill: Bill): boolean {
  return JSON.stringify(values.lines.map(lineRequest)) !== JSON.stringify(bill.lines.map(lineValues).map(lineRequest));
}

const lineSummary = (bill: Bill) =>
  bill.lines.length === 0
    ? 'none'
    : bill.lines.map((line) => `${line.description} × ${String(line.quantity)}`).join('; ');

/** The fields a conflict compares (ADR 0039 point 7): what a user sees of a draft. */
export const comparedBillFields: readonly ComparedField<Bill>[] = [
  { label: 'Bill date', value: (bill) => formatDate(bill.billDate) },
  { label: 'Due date', value: (bill) => formatDate(bill.dueDate) },
  { label: 'Encounter reference', value: (bill) => bill.encounterReference ?? '' },
  { label: 'Notes', value: (bill) => bill.notes ?? '' },
  { label: 'Lines', value: lineSummary },
  { label: 'Total', value: (bill) => formatDecimal(bill.total) },
  { label: 'Status', value: (bill) => billStatusLabels[bill.status] },
];

/** A field of the API's answer named by the position of a line (`lines[1].quantity`), as the editor names it. */
export function lineField(apiField: string): `lines.${number}.${keyof BillLineValues}` | null {
  const match = /^lines\[(\d+)\]\.(\w+)$/i.exec(apiField);
  if (match === null) {
    return null;
  }

  const [, index = '0', name = ''] = match;
  const field = (name.charAt(0).toLowerCase() + name.slice(1)) as keyof BillLineValues;
  const known: readonly string[] = [
    'serviceItemId',
    'description',
    'quantity',
    'unitPrice',
    'discountAmount',
    'taxRatePercent',
  ];
  return known.includes(field) ? `lines.${Number(index)}.${field}` : null;
}

export interface BillListParams {
  status: BillStatus | undefined;
  from: string;
  to: string;
  patientId: string;
  payerId: string;
  page: number;
}

export const billKeys = {
  all: ['bills'] as const,
  list: (params: BillListParams) => ['bills', 'list', params] as const,
  one: (id: string) => ['bills', id] as const,
};

/** After a bill changed: the lists are read again; the bill itself is set by the caller. */
export function billsChanged(queryClient: QueryClient): void {
  void queryClient.invalidateQueries({ queryKey: [...billKeys.all, 'list'] });
  void queryClient.invalidateQueries({ queryKey: ['audit', 'Bill'] });
}

export function getBill(client: ApiClient, id: string): Promise<Bill> {
  return answer(client.GET('/api/v1/bills/{id}', { params: { path: { id } } }));
}

export function createBill(client: ApiClient, body: ReturnType<typeof newBillRequest>, key: string): Promise<Bill> {
  return answer(client.POST('/api/v1/bills', { body, headers: { 'Idempotency-Key': key } }));
}

export function correctHeader(client: ApiClient, bill: Bill, values: BillValues): Promise<Bill> {
  return answer(
    client.PATCH('/api/v1/bills/{id}', {
      params: { path: { id: bill.id } },
      body: headerRequest(values),
      headers: ifMatch(bill.version),
    }),
  );
}

export function replaceLines(client: ApiClient, bill: Bill, values: BillValues): Promise<Bill> {
  return answer(
    client.PUT('/api/v1/bills/{id}/lines', {
      params: { path: { id: bill.id } },
      body: { lines: values.lines.map(lineRequest) },
      headers: ifMatch(bill.version),
    }),
  );
}

export function voidBill(client: ApiClient, bill: Bill, reason: string, key: string): Promise<Bill> {
  return answer(
    client.POST('/api/v1/bills/{id}/void', {
      params: { path: { id: bill.id } },
      body: { reason },
      headers: { ...ifMatch(bill.version), 'Idempotency-Key': key },
    }),
  );
}

/** Posts a draft: 200 posted with its number, or 202 waiting for approval (BR-207). */
export function postBill(client: ApiClient, bill: Bill, key: string): Promise<{ data: Bill; status: number }> {
  return answerWithStatus(
    client.POST('/api/v1/bills/{id}/post', {
      params: { path: { id: bill.id } },
      headers: { ...ifMatch(bill.version), 'Idempotency-Key': key },
    }),
  );
}

export function useBills(params: BillListParams) {
  const client = useApiClient();
  return useQuery({
    queryKey: billKeys.list(params),
    queryFn: () =>
      answer(
        client.GET('/api/v1/bills', {
          params: {
            query: {
              ...(params.status === undefined ? {} : { Status: params.status }),
              ...(params.from === '' ? {} : { BillDateFrom: params.from }),
              ...(params.to === '' ? {} : { BillDateTo: params.to }),
              ...(params.patientId === '' ? {} : { PatientId: params.patientId }),
              ...(params.payerId === '' ? {} : { PayerId: params.payerId }),
              Page: params.page,
              PageSize: pageSize,
            },
          },
        }),
      ),
  });
}

export function useBill(id: string) {
  const client = useApiClient();
  return useQuery({ queryKey: billKeys.one(id), queryFn: () => getBill(client, id) });
}
