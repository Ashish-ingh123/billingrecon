import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { answer } from '../api/call';
import type { ApiClient } from '../api/client';
import { useApiClient } from '../api/ApiClientContext';
import type { components } from '../api/schema';

/** The rows and totals of the reports (`docs/api.md` section 8.15, BR-901). */
type Schemas = components['schemas'];
export type BillRegisterRow = Schemas['BillRegisterRowDto'];
export type BillRegisterTotals = Schemas['BillRegisterTotalsDto'];
export type PaymentRegisterRow = Schemas['PaymentRegisterRowDto'];
export type PaymentRegisterTotals = Schemas['PaymentRegisterTotalsDto'];
export type DailyCollection = Schemas['DailyCollectionDto'];
export type CollectionByMethod = Schemas['CollectionByMethodDto'];
export type PayerOutstanding = Schemas['PayerOutstandingDto'];
export type OutstandingTotals = Schemas['OutstandingTotalsDto'];
export type UnappliedPayment = Schemas['UnappliedPaymentDto'];
export type UnappliedCashTotals = Schemas['UnappliedCashTotalsDto'];
export type AvailableCreditNote = Schemas['AvailableCreditNoteDto'];
export type AvailableCreditTotals = Schemas['AvailableCreditTotalsDto'];
export type PendingApprovalRow = Schemas['PendingApprovalDto'];
export type PendingApprovalsTotals = Schemas['PendingApprovalsTotalsDto'];
export type DriftRow = Schemas['DriftRowDto'];
export type ReconciliationTotals = Schemas['ReconciliationTotalsDto'];
export type ReportBillStatus = Schemas['ReportBillStatus'];
export type ReportPaymentStatus = Schemas['ReportPaymentStatus'];
export type ReportPaymentMethod = Schemas['ReportPaymentMethod'];
export type ReportOrder = 'OldestFirst' | 'NewestFirst';
export type ApprovalDocumentType = Schemas['ApprovalDocumentType'];

/** A period of business dates in India; both empty is the current month, as the API has it. */
export interface Period {
  from: string;
  to: string;
}

const periodQuery = ({ from, to }: Period) => (from !== '' && to !== '' ? { From: from, To: to } : {});
const optional = <K extends string>(name: K, value: string | undefined) =>
  value === undefined || value === '' ? {} : ({ [name]: value } as Record<K, string>);

export interface BillRegisterFilter extends Period {
  payerId: string;
  status: ReportBillStatus | undefined;
}

export function readBillRegister(client: ApiClient, filter: BillRegisterFilter, page: number, pageSize: number) {
  return answer(
    client.GET('/api/v1/reports/bill-register', {
      params: {
        query: {
          ...periodQuery(filter),
          ...optional('PayerId', filter.payerId),
          ...optional('Status', filter.status),
          Page: page,
          PageSize: pageSize,
        },
      },
    }),
  );
}

export interface PaymentRegisterFilter extends Period {
  payerId: string;
  method: ReportPaymentMethod | undefined;
  status: ReportPaymentStatus | undefined;
  order: ReportOrder;
}

export function readPaymentRegister(client: ApiClient, filter: PaymentRegisterFilter, page: number, pageSize: number) {
  return answer(
    client.GET('/api/v1/reports/payment-register', {
      params: {
        query: {
          ...periodQuery(filter),
          ...optional('PayerId', filter.payerId),
          ...optional('Method', filter.method),
          ...optional('Status', filter.status),
          ...(filter.order === 'NewestFirst' ? { Order: 'NewestFirst' as const } : {}),
          Page: page,
          PageSize: pageSize,
        },
      },
    }),
  );
}

export function readDailyCollection(client: ApiClient, date: string) {
  return answer(client.GET('/api/v1/reports/daily-collection', { params: { query: date === '' ? {} : { date } } }));
}

export function readOutstanding(client: ApiClient, search: string, page: number, pageSize: number) {
  return answer(
    client.GET('/api/v1/reports/outstanding-by-payer', {
      params: { query: { ...optional('Search', search.trim()), Page: page, PageSize: pageSize } },
    }),
  );
}

export function readUnappliedCash(client: ApiClient, payerId: string, page: number, pageSize: number) {
  return answer(
    client.GET('/api/v1/reports/unapplied-cash', {
      params: { query: { ...optional('PayerId', payerId), Page: page, PageSize: pageSize } },
    }),
  );
}

export function readAvailableCredit(client: ApiClient, payerId: string, page: number, pageSize: number) {
  return answer(
    client.GET('/api/v1/reports/available-credit', {
      params: { query: { ...optional('PayerId', payerId), Page: page, PageSize: pageSize } },
    }),
  );
}

export function readPendingApprovals(
  client: ApiClient,
  documentType: ApprovalDocumentType | undefined,
  page: number,
  pageSize: number,
) {
  return answer(
    client.GET('/api/v1/reports/pending-approvals', {
      params: { query: { ...optional('DocumentType', documentType), Page: page, PageSize: pageSize } },
    }),
  );
}

export function readReconciliation(client: ApiClient, page: number, pageSize: number) {
  return answer(client.GET('/api/v1/reports/reconciliation', { params: { query: { page, pageSize } } }));
}

/**
 * A page of a report for the screen, read again whenever the filter or the page changes; the page shown stays while the
 * next one is read. Reports are never cached across visits: each opening reads the figures as they are now.
 */
export function useReport<T>(name: string, filter: unknown, read: (client: ApiClient) => Promise<T>) {
  const client = useApiClient();
  return useQuery({
    queryKey: ['reports', name, filter],
    queryFn: () => read(client),
    placeholderData: keepPreviousData,
    staleTime: 0,
    gcTime: 0,
  });
}
