import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { answer, ifMatch } from '../api/call';
import type { ApiClient } from '../api/client';
import { useApiClient } from '../api/ApiClientContext';
import type { components } from '../api/schema';
import type { ComparedField } from '../components/conflict';
import { listQuery, type ListParams } from '../components/listParams';
import { toNumber } from '../decimals';
import { formatDecimal, formatPercent } from '../format';

/** A service item of the catalogue as the API answers one (`docs/api.md` section 8.3). */
export type ServiceItem = components['schemas']['ServiceItemDto'];

/** The fields of a service item a form edits, as text; the numbers are checked as text and sent as numbers (ADR 0039). */
export interface ServiceItemValues {
  code: string;
  name: string;
  category: string;
  defaultUnitPrice: string;
  taxRatePercent: string;
  sacCode: string;
}

export const serviceItemFields = [
  'code',
  'name',
  'category',
  'defaultUnitPrice',
  'taxRatePercent',
  'sacCode',
] as const satisfies readonly (keyof ServiceItemValues)[];

export const emptyServiceItem: ServiceItemValues = {
  code: '',
  name: '',
  category: '',
  defaultUnitPrice: '',
  taxRatePercent: '',
  sacCode: '',
};

export function serviceItemValues(item: ServiceItem): ServiceItemValues {
  return {
    code: item.code,
    name: item.name,
    category: item.category ?? '',
    defaultUnitPrice: String(item.defaultUnitPrice),
    taxRatePercent: String(item.taxRatePercent),
    sacCode: item.sacCode ?? '',
  };
}

/** The editable fields, as the API takes them: every field but the code, the two numbers as JSON numbers. */
function correction(values: ServiceItemValues) {
  return {
    name: values.name,
    category: values.category.trim() === '' ? null : values.category,
    defaultUnitPrice: toNumber(values.defaultUnitPrice),
    taxRatePercent: toNumber(values.taxRatePercent),
    sacCode: values.sacCode.trim() === '' ? null : values.sacCode.trim(),
  };
}

export const comparedServiceItemFields: readonly ComparedField<ServiceItem>[] = [
  { label: 'Name', value: (item) => item.name },
  { label: 'Category', value: (item) => item.category ?? '' },
  { label: 'Default unit price', value: (item) => formatDecimal(item.defaultUnitPrice, 4) },
  { label: 'Tax rate', value: (item) => formatPercent(item.taxRatePercent) },
  { label: 'SAC code', value: (item) => item.sacCode ?? '' },
  { label: 'Status', value: (item) => item.status },
];

export const serviceItemKeys = {
  all: ['service-items'] as const,
  list: (params: ListParams) => ['service-items', 'list', params] as const,
  one: (id: string) => ['service-items', id] as const,
};

export function getServiceItem(client: ApiClient, id: string): Promise<ServiceItem> {
  return answer(client.GET('/api/v1/service-items/{id}', { params: { path: { id } } }));
}

export function createServiceItem(client: ApiClient, values: ServiceItemValues): Promise<ServiceItem> {
  return answer(client.POST('/api/v1/service-items', { body: { code: values.code, ...correction(values) } }));
}

export function correctServiceItem(
  client: ApiClient,
  id: string,
  version: string,
  values: ServiceItemValues,
): Promise<ServiceItem> {
  return answer(
    client.PATCH('/api/v1/service-items/{id}', {
      params: { path: { id } },
      body: correction(values),
      headers: ifMatch(version),
    }),
  );
}

export function deactivateServiceItem(client: ApiClient, item: ServiceItem): Promise<ServiceItem> {
  return answer(
    client.POST('/api/v1/service-items/{id}/deactivate', {
      params: { path: { id: item.id } },
      headers: ifMatch(item.version),
    }),
  );
}

export function reactivateServiceItem(client: ApiClient, item: ServiceItem): Promise<ServiceItem> {
  return answer(
    client.POST('/api/v1/service-items/{id}/reactivate', {
      params: { path: { id: item.id } },
      headers: ifMatch(item.version),
    }),
  );
}

export function useServiceItems(params: ListParams) {
  const client = useApiClient();
  return useQuery({
    queryKey: serviceItemKeys.list(params),
    queryFn: ({ signal }) =>
      answer(client.GET('/api/v1/service-items', { params: { query: listQuery(params) }, signal })),
    // The rows on screen stay while a search that is being typed is read (ADR 0039, amended 2026-11-25).
    placeholderData: keepPreviousData,
  });
}

export function useServiceItem(id: string) {
  const client = useApiClient();
  return useQuery({ queryKey: serviceItemKeys.one(id), queryFn: () => getServiceItem(client, id) });
}
