import { keepPreviousData, useQuery, type QueryClient } from '@tanstack/react-query';
import { answer, ifMatch } from '../api/call';
import type { ApiClient } from '../api/client';
import { useApiClient } from '../api/ApiClientContext';
import type { components } from '../api/schema';
import type { ComparedField } from '../components/conflict';
import { listQuery, type ListParams } from '../components/listParams';

/** A payer as the API answers one (`docs/api.md` section 8.2). */
export type Payer = components['schemas']['PayerDto'];

/** The fields of a payer a form edits: the name and the contact details; the kind and the patient never change. */
export interface PayerValues {
  name: string;
  phone: string;
  email: string;
}

export const payerFields = ['name', 'phone', 'email'] as const satisfies readonly (keyof PayerValues)[];

export function payerValues(payer: Payer): PayerValues {
  return { name: payer.name, phone: payer.phone ?? '', email: payer.email ?? '' };
}

const blankAsNull = (value: string): string | null => (value.trim() === '' ? null : value);

/** How a payer's kind reads: Self-pay is the only one in R1 (BR-103). */
export function kindLabel(kind: Payer['kind']): string {
  return kind === 'SelfPay' ? 'Self-pay' : kind;
}

export const comparedPayerFields: readonly ComparedField<Payer>[] = [
  { label: 'Name', value: (payer) => payer.name },
  { label: 'Phone', value: (payer) => payer.phone ?? '' },
  { label: 'E-mail address', value: (payer) => payer.email ?? '' },
  { label: 'Status', value: (payer) => payer.status },
];

export const payerKeys = {
  all: ['payers'] as const,
  list: (params: ListParams) => ['payers', 'list', params] as const,
  one: (id: string) => ['payers', id] as const,
  ofPatient: (patientId: string) => ['payers', 'of-patient', patientId] as const,
};

/** After a payer changed: the lists and the payer of each patient are read again; the payer itself is set by the caller. */
export function payersChanged(queryClient: QueryClient): void {
  void queryClient.invalidateQueries({ queryKey: [...payerKeys.all, 'list'] });
  void queryClient.invalidateQueries({ queryKey: [...payerKeys.all, 'of-patient'] });
}

export function getPayer(client: ApiClient, id: string): Promise<Payer> {
  return answer(client.GET('/api/v1/payers/{id}', { params: { path: { id } } }));
}

/** Creates a self-pay payer: the patient's own when `patientId` is given, a guarantor otherwise (BR-103, BR-104). */
export function createPayer(client: ApiClient, values: PayerValues, patientId: string | null): Promise<Payer> {
  return answer(
    client.POST('/api/v1/payers', {
      body: {
        kind: 'SelfPay',
        patientId,
        name: values.name,
        phone: blankAsNull(values.phone),
        email: blankAsNull(values.email),
      },
    }),
  );
}

export function correctPayer(client: ApiClient, id: string, version: string, values: PayerValues): Promise<Payer> {
  return answer(
    client.PATCH('/api/v1/payers/{id}', {
      params: { path: { id } },
      body: { name: values.name, phone: blankAsNull(values.phone), email: blankAsNull(values.email) },
      headers: ifMatch(version),
    }),
  );
}

export function deactivatePayer(client: ApiClient, payer: Payer): Promise<Payer> {
  return answer(
    client.POST('/api/v1/payers/{id}/deactivate', {
      params: { path: { id: payer.id } },
      headers: ifMatch(payer.version),
    }),
  );
}

export function reactivatePayer(client: ApiClient, payer: Payer): Promise<Payer> {
  return answer(
    client.POST('/api/v1/payers/{id}/reactivate', {
      params: { path: { id: payer.id } },
      headers: ifMatch(payer.version),
    }),
  );
}

export function usePayers(params: ListParams) {
  const client = useApiClient();
  return useQuery({
    queryKey: payerKeys.list(params),
    queryFn: ({ signal }) => answer(client.GET('/api/v1/payers', { params: { query: listQuery(params) }, signal })),
    // The rows on screen stay while a search that is being typed is read (ADR 0039, amended 2026-11-25).
    placeholderData: keepPreviousData,
  });
}

/** A payer; with `enabled` false it reads nothing (a filter with no payer chosen). */
export function usePayer(id: string, enabled = true) {
  const client = useApiClient();
  return useQuery({ queryKey: payerKeys.one(id), queryFn: () => getPayer(client, id), enabled });
}

/** The payer linked to a patient, or null when there is none (BR-104: at most one). */
export function usePayerOfPatient(patientId: string) {
  const client = useApiClient();
  return useQuery({
    queryKey: payerKeys.ofPatient(patientId),
    queryFn: async () => {
      const page = await answer(
        client.GET('/api/v1/payers', { params: { query: { PatientId: patientId, PageSize: 1 } } }),
      );
      return page.items[0] ?? null;
    },
  });
}
