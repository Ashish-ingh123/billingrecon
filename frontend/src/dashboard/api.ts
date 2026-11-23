import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { answer } from '../api/call';
import { useApiClient } from '../api/ApiClientContext';
import type { components } from '../api/schema';

/** The dashboard's answers (`docs/api.md` section 8.16, BR-902): one request per panel, each loading and failing alone. */
type Schemas = components['schemas'];
export type DashboardSummary = Schemas['DashboardSummaryDto'];
export type SummaryFigure = Schemas['SummaryFigureDto'];
export type DashboardTrend = Schemas['DashboardTrendDto'];
export type DailyFigures = Schemas['DailyFiguresDto'];
export type Ageing = Schemas['AgeingDto'];

/** A period of business dates in India; both empty is the API's default, the last 30 days ending today. */
export interface DashboardPeriod {
  from: string;
  to: string;
}

const periodQuery = ({ from, to }: DashboardPeriod) => (from !== '' && to !== '' ? { from, to } : {});

function usePanel<T>(name: string, key: unknown, read: () => Promise<T>) {
  return useQuery({
    queryKey: ['dashboard', name, key],
    queryFn: read,
    placeholderData: keepPreviousData,
    staleTime: 0,
    gcTime: 0,
    retry: false,
  });
}

export function useSummary(period: DashboardPeriod) {
  const client = useApiClient();
  return usePanel('summary', period, () =>
    answer(client.GET('/api/v1/dashboard/summary', { params: { query: periodQuery(period) } })),
  );
}

export function useTrend(period: DashboardPeriod) {
  const client = useApiClient();
  return usePanel('trend', period, () =>
    answer(client.GET('/api/v1/dashboard/trend', { params: { query: periodQuery(period) } })),
  );
}

export function useAgeing() {
  const client = useApiClient();
  return usePanel('ageing', null, () => answer(client.GET('/api/v1/dashboard/ageing')));
}

export function useTopPayers() {
  const client = useApiClient();
  return usePanel('top-payers', null, () => answer(client.GET('/api/v1/dashboard/top-payers')));
}

export function useRecentPayments(period: DashboardPeriod) {
  const client = useApiClient();
  return usePanel('recent-payments', period, () =>
    answer(client.GET('/api/v1/dashboard/recent-payments', { params: { query: periodQuery(period) } })),
  );
}

export function usePendingPanel() {
  const client = useApiClient();
  return usePanel('pending-approvals', null, () => answer(client.GET('/api/v1/dashboard/pending-approvals')));
}

export function useDriftPanel() {
  const client = useApiClient();
  return usePanel('drift', null, () => answer(client.GET('/api/v1/dashboard/drift')));
}
